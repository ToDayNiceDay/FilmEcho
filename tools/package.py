from pathlib import Path
import shutil, zipfile, hashlib, json
root=Path(__file__).resolve().parent.parent
out=root/'FilmEcho免安装版'
out.mkdir(exist_ok=True)
items=['英语跟读播放器.exe','FilmEcho.Updater.exe','update-source.json','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','使用说明.md','第三方组件.md','开发说明.md','验证报告.md','更新功能接入说明.md','LICENSE.txt','tools/prepare-update.ps1','tools/verify-updater.ps1']
items += [str(p.relative_to(root)) for folder in ['source','assets','licenses','vendor/dictionary'] for p in (root/folder).rglob('*') if p.is_file()]
items += ['vendor/mpv/mpv.exe','vendor/mpv/d3dcompiler_43.dll','verification/results.txt','verification/ui-results.txt','verification/real-movies.txt','verification/theme-results.txt','verification/controls-results.txt']
items += [str(p.relative_to(root)) for p in (root/'verification').glob('interface-*.png')]
items += ['verification/query-glyph.png','verification/query-glyph-reference.png']
items += ['verification/usage-results.txt']
items += ['verification/focus-results.txt']
items += ['verification/lookup-results.txt']
items += ['verification/welcome-results.txt']
items += ['verification/update-results.txt','verification/updater-process-results.txt']
items += ['verification/bundle-update-results.txt']
items += [str(p.relative_to(root)) for p in (root/'verification').glob('update-*.png')]
manifest=[]
for item in items:
    src=root/item; dst=out/item; dst.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(src,dst)
    manifest.append({'path':str(Path(item)).replace('\\','/'),'sha256':hashlib.sha256(src.read_bytes()).hexdigest(),'bytes':src.stat().st_size})
(out/'files.sha256.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
archive=root/'FilmEcho英语跟读播放器-免安装.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for item in sorted(set(items+['files.sha256.json'])):
        p=out/item
        z.write(p,str(p.relative_to(root)))
with zipfile.ZipFile(archive) as z:
    bad=z.testzip(); assert bad is None,bad
    assert not any('/data/' in n.replace('\\','/') for n in z.namelist()), 'Personal data included'
    assert set(z.namelist()) == {str((out/item).relative_to(root)).replace('\\','/') for item in set(items+['files.sha256.json'])}, 'Unexpected files included'
for entry in manifest:
    assert hashlib.sha256((out/entry['path']).read_bytes()).hexdigest()==entry['sha256']
print(f'Package verified: {len(manifest)} files; ZIP {archive.stat().st_size:,} bytes')
