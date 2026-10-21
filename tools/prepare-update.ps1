param(
    [Parameter(Mandatory=$true)][string]$PrivateKeyFile,
    [string]$Repository='',
    [string]$OutputDirectory=''
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$keyPath=(Resolve-Path -LiteralPath $PrivateKeyFile).Path
$projectBoundary=[IO.Path]::GetFullPath($projectRoot).TrimEnd('\')+'\'
if ($keyPath.StartsWith($projectBoundary,[StringComparison]::OrdinalIgnoreCase)) { throw 'Keep the private signing key outside the project and distribution directories.' }
$appFile=Join-Path $projectRoot '英语跟读播放器.exe'
$version=[Version][Diagnostics.FileVersionInfo]::GetVersionInfo($appFile).FileVersion
$versionText=if ($version.Revision -gt 0) { $version.ToString(4) } else { $version.ToString(3) }
if (!$OutputDirectory) { $OutputDirectory=Join-Path $projectRoot ('output\releases\'+$versionText) }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory=(Resolve-Path -LiteralPath $OutputDirectory).Path
$files=New-Object System.Collections.Generic.List[string]
foreach ($name in @('英语跟读播放器.exe','FilmEcho.Updater.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','使用说明.md','开发说明.md','第三方组件.md','验证报告.md','更新功能接入说明.md','LICENSE.txt')) { $files.Add($name) }
foreach ($folder in @('source','assets','licenses','vendor\dictionary')) {
    Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File -Recurse | ForEach-Object { $files.Add($_.FullName.Substring($projectBoundary.Length).Replace('\','/')) }
}
foreach ($name in @('vendor/mpv/mpv.exe','vendor/mpv/d3dcompiler_43.dll')) { $files.Add($name) }
$fileEntries=@()
$packageName='FilmEcho-'+$versionText+'-win-x64.zip'
$packagePath=Join-Path $OutputDirectory $packageName
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipStream=[IO.File]::Open($packagePath,[IO.FileMode]::Create,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
    $zip=[IO.Compression.ZipArchive]::new($zipStream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try {
        foreach ($relative in ($files | Sort-Object -Unique)) {
            $inputPath=Join-Path $projectRoot $relative
            $inputInfo=Get-Item -LiteralPath $inputPath
            $entry=$zip.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
            $inputStream=[IO.File]::OpenRead($inputPath); $outputStream=$entry.Open()
            try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose(); $outputStream.Dispose() }
            $fileEntries+=@{ Path=$relative; Size=$inputInfo.Length; Sha256=(Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
    } finally { $zip.Dispose() }
} finally { $zipStream.Dispose() }
$manifest=@{ Schema=1; Version=$versionText; PackageName=$packageName; Size=(Get-Item -LiteralPath $packagePath).Length; Sha256=(Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant(); Files=$fileEntries }
$payload=[Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 8 -Compress))
$signer=New-Object Security.Cryptography.RSACryptoServiceProvider
$signer.PersistKeyInCsp=$false
try {
    $privateXml=[IO.File]::ReadAllText($keyPath)
    if (!$privateXml.Contains('<D>')) { throw 'A private RSA XML signing key is required.' }
    $signer.FromXmlString($privateXml)
    if ($signer.KeySize -lt 2048) { throw 'RSA signing keys must be at least 2048 bits.' }
    $signature=$signer.SignData($payload,'SHA256')
    $envelope=@{ Payload=[Convert]::ToBase64String($payload); Signature=[Convert]::ToBase64String($signature) } | ConvertTo-Json -Compress
    $example=@{ Repository=$Repository; PublicKeyXml=$signer.ToXmlString($false) } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'FilmEcho-update.json'),$envelope,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'update-source.example.json'),$example,[Text.UTF8Encoding]::new($false))
} finally { $privateXml=$null; $signer.Dispose() }
Write-Output ('Prepared locally: '+$packagePath)
Write-Output ('Signed manifest: '+(Join-Path $OutputDirectory 'FilmEcho-update.json'))
Write-Output 'No upload or GitHub publication was performed. The example configuration contains only the public key.'
