param([switch]$ConfigureOnly)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$appDirectory=Join-Path $projectRoot 'output\public\v1.4.3'
$releaseDirectory=Join-Path $projectRoot 'output\github-release\v1.4.3'
$privateDirectory=Join-Path $projectRoot 'output\private'
$protectedFile=Join-Path $privateDirectory 'filmecho-update-key.dpapi'
New-Item -ItemType Directory -Path $privateDirectory,$appDirectory -Force | Out-Null
Add-Type -AssemblyName System.Security
$signer=New-Object Security.Cryptography.RSACryptoServiceProvider(3072)
$signer.PersistKeyInCsp=$false
$plainBytes=$null
try {
    if(Test-Path -LiteralPath $protectedFile) {
        $plainBytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($protectedFile),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        $signer.FromXmlString([Text.Encoding]::UTF8.GetString($plainBytes))
    } else {
        $plainBytes=[Text.Encoding]::UTF8.GetBytes($signer.ToXmlString($true))
        $encrypted=[Security.Cryptography.ProtectedData]::Protect($plainBytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        [IO.File]::WriteAllBytes($protectedFile,$encrypted)
    }
    $source=@{ Repository='ToDayNiceDay/FilmEcho'; PublicKeyXml=$signer.ToXmlString($false) } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $appDirectory 'update-source.json'),$source,[Text.UTF8Encoding]::new($false))
    if(!$ConfigureOnly) {
        $package=Get-Content -LiteralPath (Join-Path $releaseDirectory 'package-manifest.json') -Raw | ConvertFrom-Json
        $payload=[Text.Encoding]::UTF8.GetBytes(($package.update | ConvertTo-Json -Depth 8 -Compress))
        $envelope=@{ Payload=[Convert]::ToBase64String($payload); Signature=[Convert]::ToBase64String($signer.SignData($payload,'SHA256')) } | ConvertTo-Json -Compress
        [IO.File]::WriteAllText((Join-Path $releaseDirectory 'FilmEcho-update.json'),$envelope,[Text.UTF8Encoding]::new($false))
        Write-Output 'Public runtime update manifest signed locally.'
    }
    Write-Output 'Official repository configured. Only the public verification key is included in the application.'
} finally {
    if($plainBytes) { [Array]::Clear($plainBytes,0,$plainBytes.Length) }
    $signer.Dispose()
}
