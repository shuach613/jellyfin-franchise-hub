#!/usr/bin/env python3
"""Test the independent catalog publisher without publishing or changing repo files."""
from pathlib import Path
import hashlib, zipfile, subprocess, sys, tempfile, json
root=Path(__file__).resolve().parents[1]
repo=root.parent
identity=json.loads((repo/'project-identity.json').read_text())
publisher=root/'scripts/publish_manifest.py'
with tempfile.TemporaryDirectory() as td:
    d=Path(td)
    (d/'manifest.json').write_text((repo/'manifest.json').read_text())
    z=d/f'franchise-hub_{identity["version"]}.zip'
    with zipfile.ZipFile(z,'w') as out:
        out.writestr(identity['assemblyName']+'.dll',b'test-not-a-real-assembly'*100)
    cmd=[sys.executable,str(publisher),'--version',identity['version'],'--archive',str(z),'--repository',identity['repository'],'--manifest',str(d/'manifest.json')]
    subprocess.run(cmd,cwd=d,check=True)
    obj=json.loads((d/'manifest.json').read_text())
    assert len(obj)==1 and len(obj[0]['versions'])==1
    version=obj[0]['versions'][0]
    assert version['checksum']==hashlib.md5(z.read_bytes()).hexdigest()
    assert version['targetAbi']=='12.0.0.0'
    assert version['sourceUrl'].startswith('https://github.com/'+identity['repository'])
    assert version['sourceUrl'].endswith(z.name)
    assert subprocess.run(cmd,cwd=d,capture_output=True).returncode!=0, 'Duplicate versions must be rejected'
    assert subprocess.run(cmd[:7]+['Gost1o1/Jellyfin-Franchise-Explorer']+cmd[8:],cwd=d,capture_output=True).returncode!=0, 'Foreign repository must be rejected'
    assert json.loads((repo/'manifest.json').read_text())[0]['versions']==[], 'Do not publish during tests'
print('Offline independent manifest, checksum and foreign-repo rejection checks passed.')
