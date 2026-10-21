param([switch]$PublicRelease,[string]$OutputDirectory='')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destination=if($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } elseif($PublicRelease) { Join-Path $root 'output\public\v1.4.3' } else { $root }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$defines=if($PublicRelease) { @('/define:PUBLIC_RELEASE') } else { @() }
$sdk = (Get-ChildItem 'C:\Program Files\dotnet\sdk' -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs = @('mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Web.Extensions','System.Net.Http','System.IO.Compression','System.IO.Compression.FileSystem','Microsoft.CSharp') | ForEach-Object { '/r:' + (Join-Path $framework ($_ + '.dll')) }
& dotnet (Join-Path $sdk 'Roslyn\bincore\csc.dll') /nologo /target:winexe /platform:x64 /langversion:latest /nullable:disable /nostdlib+ /optimize+ ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/out:' + (Join-Path $destination 'FilmEcho.Updater.exe')) $refs (Join-Path $PSScriptRoot 'UpdateCore.cs') (Join-Path $PSScriptRoot 'updater\Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Updater compilation failed' }
$wv = Join-Path $root 'tools\webview2\lib\net462'
if (!(Test-Path -LiteralPath $wv)) { $wv=$root }
if(!$PublicRelease) {
    $refs += '/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.Core.dll')
    $refs += '/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.WinForms.dll')
}
$mapResource='/resource:' + (Join-Path $root 'assets\maps\china-provinces.geojson') + ',FilmEcho.ChinaProvinces.geojson'
$sourceFiles=Get-ChildItem $PSScriptRoot -Filter '*.cs'
if($PublicRelease) { $sourceFiles=$sourceFiles | Where-Object { $_.Name -notlike '*Verification.cs' -or $_.Name -in @('UpdateVerification.cs','PublicReleaseVerification.cs') } }
$resources=@($mapResource)
if($PublicRelease) { $resources+='/resource:'+(Join-Path $PSScriptRoot 'ocr.ps1')+',FilmEcho.OcrScript' }
& dotnet (Join-Path $sdk 'Roslyn\bincore\csc.dll') /nologo /target:winexe /platform:x64 /langversion:latest /nullable:disable /nostdlib+ /optimize+ $defines ('/win32icon:' + (Join-Path $root 'assets\FilmEcho.ico')) ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) $resources ('/out:' + (Join-Path $destination '英语跟读播放器.exe')) $refs ($sourceFiles | ForEach-Object FullName)
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
if(!$PublicRelease) {
    if ($wv -ne $root) { Copy-Item -LiteralPath (Join-Path $wv 'Microsoft.Web.WebView2.Core.dll'),(Join-Path $wv 'Microsoft.Web.WebView2.WinForms.dll') -Destination $destination -Force }
    if(Test-Path (Join-Path $root 'tools\webview2\runtimes\win-x64\native\WebView2Loader.dll')) { Copy-Item -LiteralPath (Join-Path $root 'tools\webview2\runtimes\win-x64\native\WebView2Loader.dll') -Destination $destination -Force }
}
Write-Output 'Build succeeded'
