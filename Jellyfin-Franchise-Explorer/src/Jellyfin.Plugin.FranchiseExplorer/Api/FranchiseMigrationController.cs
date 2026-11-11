using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.FranchiseExplorer.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.FranchiseExplorer.Api;

/// <summary>
/// Administrator-only, explicit migration of locally staged *copies* of old
/// timeline JSON. Never reads the old plugin directory by guesswork, accepts
/// arbitrary filesystem paths, or modifies legacy playback history.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("FranchiseHub/v1/migration")]
public sealed class FranchiseMigrationController(
    IUserManager userManager, LegacyTimelineMigrationService migration)
    : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public sealed class ApplyMigrationRequest
    {
        public int SchemaVersion { get; set; } = 1;
        public string ExpectedSourceDigest { get; set; } = string.Empty;
        public long ExpectedConfigurationRevision { get; set; }
        public bool ConfirmBackupAndImport { get; set; }
    }
    public sealed class RollbackMigrationRequest
    {
        public int SchemaVersion { get; set; } = 1;
        public string SourceDigest { get; set; } = string.Empty;
        public long ExpectedConfigurationRevision { get; set; }
        public bool ConfirmRollback { get; set; }
    }
    private bool IsAdmin()
        => Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value, out var id) &&
            userManager.GetUserById(id)?.HasPermission(PermissionKind.IsAdministrator) == true;
    private IActionResult SafeJson(int status, object data)
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return new ContentResult
        {
            ContentType = "application/json; charset=utf-8",
            StatusCode = status, Content = JsonSerializer.Serialize(data, Json)
        };
    }
    [HttpGet("preview")]
    public IActionResult Preview()
    {
        if (!IsAdmin()) return Forbid();
        var plugin = Plugin.Instance;
        if (plugin is null) return SafeJson(503, new { code = "plugin_unavailable" });
        try
        {
            var result = migration.Preview(plugin.Configuration);
            return SafeJson(200, new
            {
                schemaVersion = 1, result.SourceDigest, result.Records,
                result.Ready, result.PendingReview, result.Warnings,
                result.AlreadyImported,
                configurationRevision = plugin.Configuration.ConfigurationRevision,
                stagingRequired = "legacy-staging/timeline-progress/<user-guid-N>/<row-guid-N>.json",
                autoAppliesLegacyConfiguration = false
            });
        }
        catch (InvalidDataException) { return SafeJson(422, new { code = "invalid_legacy_staging", message = "The staged files require repair." }); }
        catch (IOException) { return SafeJson(503, new { code = "migration_storage_unavailable" }); }
        catch (SqliteException) { return SafeJson(503, new { code = "migration_database_unavailable" }); }
    }

    [HttpPost("apply")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Apply([FromBody] ApplyMigrationRequest? request, CancellationToken cancellationToken)
    {
        if (!IsAdmin()) return Forbid();
        var plugin = Plugin.Instance;
        if (plugin is null) return SafeJson(503, new { code = "plugin_unavailable" });
        if (request is null || request.SchemaVersion != 1 ||
            !request.ConfirmBackupAndImport || request.ExpectedSourceDigest.Length != 64)
            return SafeJson(400, new { code = "explicit_confirmation_required" });
        // Serialize against administration edits so a changed franchise hierarchy
        // cannot be used to map progress in the middle of a migration.
        await FranchiseConfigurationGate.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (request.ExpectedConfigurationRevision != plugin.Configuration.ConfigurationRevision)
                return SafeJson(409, new { code = "stale_revision", message = "The franchise configuration changed." });
            try
            {
                // Does not modify legacy JSON, old plugin data, current XML or media.
                // Writes only a new private ZIP backup and one new-state DB transaction.
                var result = migration.Apply(plugin.Configuration,
                    request.ExpectedSourceDigest, plugin.ReadConfigurationBackup());
                return SafeJson(200, new
                {
                    schemaVersion = 1, result.AlreadyImported, result.ImportedRecords,
                    result.PendingReview, result.BackupName, result.SourceDigest
                });
            }
            catch (InvalidOperationException)
            {
                return SafeJson(409, new { code = "migration_conflict",
                    message = "Migration preview or existing progress changed; review before retrying." });
            }
            catch (InvalidDataException)
            {
                return SafeJson(422, new { code = "invalid_legacy_staging",
                    message = "Migration files are invalid or exceed allowed limits." });
            }
            catch (IOException) { return SafeJson(503, new { code = "migration_storage_unavailable" }); }
            catch (SqliteException) { return SafeJson(503, new { code = "migration_database_unavailable" }); }
        }
        finally { FranchiseConfigurationGate.Gate.Release(); }
    }

    [HttpPost("rollback")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Rollback([FromBody] RollbackMigrationRequest? request, CancellationToken cancellationToken)
    {
        if (!IsAdmin()) return Forbid();
        var plugin = Plugin.Instance;
        if (plugin is null) return SafeJson(503, new { code = "plugin_unavailable" });
        if (request is null || request.SchemaVersion != 1 ||
            !request.ConfirmRollback || request.SourceDigest.Length != 64)
            return SafeJson(400, new { code = "explicit_confirmation_required" });
        await FranchiseConfigurationGate.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (request.ExpectedConfigurationRevision != plugin.Configuration.ConfigurationRevision)
                return SafeJson(409, new { code = "stale_revision", message = "The franchise configuration changed." });
            try
            {
                var result = migration.Rollback(request.SourceDigest);
                return SafeJson(200, new
                {
                    schemaVersion = 1, result.AlreadyRolledBack,
                    result.DeletedJourneyRows, result.BackupName, result.SourceDigest,
                    legacySourcesUnchanged = true
                });
            }
            catch (InvalidOperationException)
            {
                return SafeJson(409, new { code = "rollback_conflict",
                    message = "Migrated progress changed after import; manual review is required." });
            }
            catch (IOException) { return SafeJson(503, new { code = "migration_storage_unavailable" }); }
            catch (SqliteException) { return SafeJson(503, new { code = "migration_database_unavailable" }); }
        }
        finally { FranchiseConfigurationGate.Gate.Release(); }
    }

}
