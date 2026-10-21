"""Stage and audit the public runtime package. Never copy source or user data."""
from pathlib import Path
import hashlib, json, shutil, zipfile

root=Path(__file__).resolve().parent.parent
out=root/'output'/'public'/'v1.4.3'
out.mkdir(parents=True,exist_ok=True)
required=['英语跟读播放器.exe','FilmEcho.Updater.exe','update-source.json']
for name in required:
    if not (out/name).is_file():
        raise RuntimeError(f'Build/configure the public version first: {name}')
copy_items={
    'source/input.conf':'runtime/input.conf',
    'vendor/mpv/mpv.exe':'vendor/mpv/mpv.exe',
    'vendor/mpv/d3dcompiler_43.dll':'vendor/mpv/d3dcompiler_43.dll',
    'vendor/dictionary/ecdict.tsv.gz':'vendor/dictionary/ecdict.tsv.gz',
    'LICENSE.txt':'LICENSE.txt',
    'licenses/mpv-GPL.txt':'licenses/mpv-GPL.txt',
    'licenses/mpv-LGPL.txt':'licenses/mpv-LGPL.txt',
    'licenses/mpv-Copyright.txt':'licenses/mpv-Copyright.txt',
    'licenses/mpv-Windows-build-source.md':'licenses/mpv-Windows-build-source.md',
    'licenses/ECDICT-MIT.txt':'licenses/ECDICT-MIT.txt',
    'licenses/china-boundaries-source.md':'licenses/china-boundaries-source.md',
    'release-docs/使用说明.md':'使用说明.md',
    'release-docs/第三方组件.md':'第三方组件.md',
}
for source,target in copy_items.items():
    path=out/target; path.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(root/source,path)
items=sorted(required+list(copy_items.values()))
manifest=[{'path':name,'sha256':hashlib.sha256((out/name).read_bytes()).hexdigest(),'bytes':(out/name).stat().st_size} for name in items]
(out/'files.sha256.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
release=root/'output'/'github-release'/'v1.4.3'; release.mkdir(parents=True,exist_ok=True)
archive=release/'FilmEcho-1.4.3-win-x64.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for name in items+['files.sha256.json']:
        info=zipfile.ZipInfo(name,(2026,10,14,0,0,0)); info.compress_type=zipfile.ZIP_DEFLATED
        info.external_attr=0o100644<<16; info.create_system=3
        z.writestr(info,(out/name).read_bytes())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    assert set(z.namelist())==set(items+['files.sha256.json'])
    assert not any(n.split('/')[0] in {'source','data','logs','verification','tools','.updates'} for n in z.namelist())
    assert not any(n.lower().endswith(('.cs','.ps1','.py','.pdb','.sln','.csproj','.mkv','.mp4')) or 'webview' in n.lower() for n in z.namelist())
    for entry in manifest:
        assert hashlib.sha256(z.read(entry['path'])).hexdigest()==entry['sha256']
checksum=hashlib.sha256(archive.read_bytes()).hexdigest()
update_archive=release/'FilmEcho-1.4.3-update-win-x64.zip'
update_items=[name for name in items+['files.sha256.json'] if name!='update-source.json']
with zipfile.ZipFile(update_archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for name in update_items:
        info=zipfile.ZipInfo(name,(2026,10,14,0,0,0)); info.compress_type=zipfile.ZIP_DEFLATED
        info.external_attr=0o100644<<16; info.create_system=3
        z.writestr(info,(out/name).read_bytes())
with zipfile.ZipFile(update_archive) as z:
    assert z.testzip() is None and set(z.namelist())==set(update_items)
update_hash=hashlib.sha256(update_archive.read_bytes()).hexdigest()
update_files=[{'Path':name,'Sha256':hashlib.sha256((out/name).read_bytes()).hexdigest(),'Size':(out/name).stat().st_size} for name in update_items]
(release/'SHA256SUMS.txt').write_text(f'{checksum}  {archive.name}\n{update_hash}  {update_archive.name}\n',encoding='utf-8')
(release/'package-manifest.json').write_text(json.dumps({'version':'1.4.3','file_count':len(items)+1,'zip_sha256':checksum,'zip_bytes':archive.stat().st_size,'files':manifest,'update':{'Schema':1,'Version':'1.4.3','PackageName':update_archive.name,'Size':update_archive.stat().st_size,'Sha256':update_hash,'Files':update_files}},ensure_ascii=False,indent=2),encoding='utf-8')
print(f'Public package verified: {len(items)+1} files; ZIP {archive.stat().st_size:,} bytes; no application source, data, logs, tests or WebView2')
print(archive)
