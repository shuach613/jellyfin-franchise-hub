#!/usr/bin/env python3
"""Future release catalog utility — fail closed unless archive and owner are verified."""
from pathlib import Path
from datetime import datetime, timezone
from zipfile import ZipFile
import argparse, hashlib, json, re, sys

p=argparse.ArgumentParser()
p.add_argument('--version',required=True)
p.add_argument('--archive',type=Path,required=True)
p.add_argument('--repository',required=True)
p.add_argument('--manifest',type=Path,default=Path('manifest.json'),
    help='Catalog output path. Defaults to the current working directory.')
a=p.parse_args()
repo=Path(__file__).resolve().parents[2]
identity=json.loads((repo/'project-identity.json').read_text(encoding='utf-8'))
if not re.fullmatch(r'\d+\.\d+\.\d+\.\d+',a.version) or a.version!=identity['version']:sys.exit('Version mismatch')
if a.repository!=identity['repository']:sys.exit('Refusing to publish to foreign repository')
if a.archive.name!=f'franchise-hub_{a.version}.zip':sys.exit('Unrecognized release package name')
if not a.archive.is_file() or a.archive.stat().st_size<1000:sys.exit('Release archive missing or too small')
with ZipFile(a.archive) as archive:
    if archive.namelist()!=[identity['assemblyName']+'.dll']:sys.exit('Expected only the independent plugin DLL in archive')
checksum=hashlib.md5(a.archive.read_bytes()).hexdigest() # Jellyfin manifest expects MD5
manifest_file=a.manifest
manifest=json.loads(manifest_file.read_text(encoding='utf-8'))
if len(manifest)!=1 or manifest[0]['guid']!=identity['pluginId'] or manifest[0]['owner']!='shuach613':
    sys.exit('Foreign catalog detected')
versions=manifest[0].setdefault('versions',[])
if any(v['version']==a.version for v in versions):sys.exit('Release version already in catalog')
versions.insert(0,{'version':a.version,'changelog':f'https://github.com/{a.repository}/releases/tag/v{a.version}','targetAbi':'12.0.0.0','sourceUrl':f'https://github.com/{a.repository}/releases/download/v{a.version}/{a.archive.name}','checksum':checksum,'timestamp':datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')})
manifest_file.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print('Independent catalog prepared:',a.version,checksum)
