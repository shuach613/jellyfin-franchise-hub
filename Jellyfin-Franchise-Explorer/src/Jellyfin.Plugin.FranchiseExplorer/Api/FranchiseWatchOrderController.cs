using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.FranchiseExplorer.Models;
using Jellyfin.Plugin.FranchiseExplorer.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.FranchiseExplorer.Api;

/// <summary>
/// Step 5 read-only server-resolved chronological preview. Does not change any
/// existing Web player or timeline progress. New v1 playback sessions are Step 8.
/// </summary>
[ApiController]
[Authorize]
[Route("FranchiseHub/v1")]
public sealed class FranchiseWatchOrderController(
    ILibraryManager library, IUserManager userManager, IWatchOrderResolver resolver,
    FranchiseHubStateStore stateStore)
    : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed class ResolveRequest
    {
        public int SchemaVersion { get; set; } = 1;
        public string Mode { get; set; } = "chronological";
        public long? ExpectedOrderRevision { get; set; }
    }

    private IActionResult SafeJson(int status, object value)
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return new ContentResult
        {
            StatusCode = status, ContentType = "application/json; charset=utf-8",
            Content = JsonSerializer.Serialize(value, Json)
        };
    }

    [HttpPost("franchises/{franchiseId:guid}/resolve")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Resolve(Guid franchiseId,
        [FromBody] ResolveRequest? request, CancellationToken cancellationToken)
    {
        var rawUserId = User.FindFirst("Jellyfin-UserId")?.Value;
        if (!Guid.TryParse(rawUserId, out var userId) || userId == Guid.Empty)
            return Unauthorized();
        var viewer = userManager.GetUserById(userId);
        if (viewer is null) return Unauthorized();

        if (request is null || request.SchemaVersion != 1)
            return SafeJson(400, new { schemaVersion = 1, code = "invalid_request",
                message = "Expected a v1 chronological preview request." });
        if (!string.Equals(request.Mode, "chronological", StringComparison.Ordinal))
            return SafeJson(422, new { schemaVersion = 1, code = "mode_not_implemented",
                message = "Step 5 previews the canonical chronological order only. Other playback modes are implemented in Step 10." });

        var plugin = Plugin.Instance;
        if (plugin is null)
            return SafeJson(503, new { schemaVersion = 1, code = "plugin_unavailable",
                message = "Franchise Hub is not ready." });
        var config = plugin.Configuration;
        if (request.ExpectedOrderRevision.HasValue &&
            request.ExpectedOrderRevision.Value != config.ConfigurationRevision)
            return SafeJson(409, new { schemaVersion = 1, code = "stale_revision",
                message = "The franchise configuration changed. Request a fresh preview.",
                currentRevision = config.ConfigurationRevision });

        try
        {
            var catalog = new JellyfinChronologyViewerCatalog(library, viewer);
            var preview = await resolver.ResolveAsync(config, franchiseId, userId, catalog, cancellationToken)
                .ConfigureAwait(false);
            // Step 6 stores only permission-filtered immutable snapshots and durable
            // identities. Watch progress remains owned by the legacy player until Step 8.
            var result = stateStore.RegisterSnapshot(preview, userId);
            if (result.Items.Count == 0)
                return SafeJson(422, new { schemaVersion = 1, code = "unplayable_selection",
                    message = "This chronological order has no accessible playable items.", warnings = result.Warnings });
            return SafeJson(200, result);
        }
        catch (ChronologyResolutionException ex)
        {
            var status = ex.Code switch
            {
                "not_found" => 404,
                "invalid_request" => 400,
                "ambiguous_primary_order" or "invalid_primary_order" => 409,
                "chronology_not_configured" or "too_many_items" or "too_many_references" => 422,
                "library_unavailable" or "library_too_large" => 503,
                _ => 422
            };
            return SafeJson(status, new { schemaVersion = 1, code = ex.Code, message = ex.Message });
        }
    }
}
