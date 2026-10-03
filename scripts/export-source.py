#!/usr/bin/env python3
"""Export patch, source/build inputs and a local binary package, without git."""
import difflib, hashlib, json, pathlib, tarfile
root = pathlib.Path(__file__).resolve().parents[1]
repo = root / 'repo'
artifacts = root / 'artifacts'
artifacts.mkdir(exist_ok=True)
excluded = {'node_modules', 'bin', 'obj', 'dist', '.angular', 'coverage', 'test-results', 'playwright-report', '__pycache__'}
def source_files(directory):
    return sorted(p for p in directory.rglob('*') if p.is_file() and not any(x in excluded for x in p.relative_to(directory).parts))
original = {}
with tarfile.open(root/'provenance/upstream.tar.gz') as archive:
    for member in archive.getmembers():
        if member.isfile():
            name=member.name.split('/',1)[1]
            original[name]=archive.extractfile(member).read()
files={str(p.relative_to(repo)):p for p in source_files(repo)}
patch=[]; manifest=[]
for name in sorted(original.keys() | files.keys()):
    old=original.get(name,b''); new=files[name].read_bytes() if name in files else b''
    if old==new: continue
    for line in difflib.unified_diff(old.decode('utf-8').splitlines(keepends=True), new.decode('utf-8').splitlines(keepends=True),
            fromfile='a/'+name if name in original else '/dev/null', tofile='b/'+name if name in files else '/dev/null'):
        patch.append(line if line.endswith('\n') else line+'\n\\ No newline at end of file\n')
    manifest.append({'path':name,'before_sha256':hashlib.sha256(old).hexdigest() if name in original else None,
        'after_sha256':hashlib.sha256(new).hexdigest() if name in files else None})
(artifacts/'native-nzbget.patch').write_text(''.join(patch))
(artifacts/'changes.json').write_text(json.dumps({'upstream_revision':'58b476c36063e116ed5c582d6ee81f90856c47ec','changes':manifest},indent=2)+'\n')
with tarfile.open(artifacts/'cleanuparr-nzbget-source.tar.gz','w:gz') as archive:
    for p in source_files(repo): archive.add(p,arcname='repo/'+str(p.relative_to(repo)),recursive=False)
    for p in source_files(root/'provenance/dependencies'):
        archive.add(p,arcname=str(p.relative_to(root)),recursive=False)
    for folder in ['scripts']:
        for p in source_files(root/folder): archive.add(p,arcname=str(p.relative_to(root)),recursive=False)
    for name in ['provenance/UPSTREAM.md','provenance/upstream.tar.gz','IMPLEMENTATION.md']:
        p=root/name
        if p.exists(): archive.add(p,arcname=name,recursive=False)
with tarfile.open(artifacts/'cleanuparr-nzbget-linux-x64.tar.gz','w:gz') as archive:
    archive.add(artifacts/'linux-x64',arcname='cleanuparr',recursive=True)
with (artifacts/'SHA256SUMS').open('w') as stream:
    for name in ['native-nzbget.patch','changes.json','cleanuparr-nzbget-source.tar.gz','cleanuparr-nzbget-linux-x64.tar.gz']:
        p=artifacts/name
        stream.write(hashlib.file_digest(p.open('rb'),'sha256').hexdigest()+'  '+name+'\n')
print(f'Exported {len(manifest)} changed/new source files')
