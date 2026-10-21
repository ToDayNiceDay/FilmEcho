param()
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$fixtureRoot=Join-Path $projectRoot ('verification\updater-process-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$sdkPath=(Get-ChildItem 'C:\Program Files\dotnet\sdk' -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$frameworkPath='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$compilerPath=Join-Path $sdkPath 'Roslyn\bincore\csc.dll'
$references=@('mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Web.Extensions','System.IO.Compression','System.IO.Compression.FileSystem') | ForEach-Object { '/r:'+(Join-Path $frameworkPath ($_+'.dll')) }
$report=New-Object System.Collections.Generic.List[string]
$failures=0
function Check([bool]$ok,[string]$message) {
    if ($ok) { $report.Add('PASS: '+$message) } else { $report.Add('FAIL: '+$message); $script:failures++ }
}
$oldSource=@'
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
[assembly: AssemblyFileVersion("1.4.1.0")]
namespace FilmEcho { internal static class UpdateMessages { public static string Error(Exception ex,bool en) { return ex.Message; } } }
class FixtureOld {
 [STAThread] static void Main(string[] args) {
  string root=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
  if(args.Length==0) { if(FilmEcho.UpdateStartup.RecoverIfNeeded(root)) return; File.WriteAllText(Path.Combine(root,"old-started.txt"),"OK"); return; }
  string job=File.ReadAllText(Path.Combine(root,"fixture-job.txt")),file=Path.Combine(job,"plan.json");
  var serializer=new JavaScriptSerializer(); var plan=serializer.Deserialize<FilmEcho.UpdatePlan>(File.ReadAllText(file));
  using(var self=Process.GetCurrentProcess()) { plan.ParentPid=self.Id; plan.ParentStarted=self.StartTime.ToUniversalTime().Ticks; }
  File.WriteAllText(file,serializer.Serialize(plan));
  using(var child=Process.Start(new ProcessStartInfo(Path.Combine(job,"FilmEcho.Updater.exe"),"--apply \""+file+"\"") { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=job })) File.WriteAllText(Path.Combine(root,"helper-pid.txt"),child.Id.ToString());
  if(args[0]=="--abort") File.WriteAllText(Path.Combine(job,"abort"),"cancelled");
  else File.WriteAllText(Path.Combine(job,"apply.ready"),"ready");
  Thread.Sleep(300);
 }
}
'@
$newSource=@'
using System;
using System.IO;
using System.Reflection;
[assembly: AssemblyFileVersion("1.4.2.0")]
class FixtureNew { [STAThread] static void Main() { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"new-started.txt"),"OK"); } }
'@
$oldSourceFile=Join-Path $fixtureRoot 'old.cs'; $newSourceFile=Join-Path $fixtureRoot 'new.cs'
Set-Content -LiteralPath $oldSourceFile -Value $oldSource -Encoding UTF8
Set-Content -LiteralPath $newSourceFile -Value $newSource -Encoding UTF8
& dotnet $compilerPath /nologo /target:winexe /platform:x64 /langversion:latest /nostdlib+ ('/out:'+(Join-Path $fixtureRoot 'old.exe')) $references (Join-Path $projectRoot 'source\UpdateCore.cs') (Join-Path $projectRoot 'source\UpdateStartup.cs') $oldSourceFile
if ($LASTEXITCODE -ne 0) { throw 'Old fixture compilation failed' }
& dotnet $compilerPath /nologo /target:winexe /platform:x64 /langversion:latest /nostdlib+ ('/out:'+(Join-Path $fixtureRoot 'new.exe')) $references $newSourceFile
if ($LASTEXITCODE -ne 0) { throw 'New fixture compilation failed' }
$signer=New-Object Security.Cryptography.RSACryptoServiceProvider(2048); $signer.PersistKeyInCsp=$false
function Prepare([string]$name) {
    $appRoot=Join-Path $fixtureRoot $name; $job=Join-Path $appRoot ('.updates\'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $job 'stage'),(Join-Path $appRoot 'data') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'old.exe') -Destination (Join-Path $appRoot '英语跟读播放器.exe')
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'new.exe') -Destination (Join-Path $job 'stage\英语跟读播放器.exe')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'FilmEcho.Updater.exe') -Destination (Join-Path $appRoot 'FilmEcho.Updater.exe')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'FilmEcho.Updater.exe') -Destination (Join-Path $job 'FilmEcho.Updater.exe')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'FilmEcho.Updater.exe') -Destination (Join-Path $job 'stage\FilmEcho.Updater.exe')
    Set-Content -LiteralPath (Join-Path $appRoot '使用说明.md') -Value 'OLD DOC' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $job 'stage\使用说明.md') -Value 'NEW DOC' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $appRoot 'data\用户记录.txt') -Value 'USER DATA' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $appRoot '用户自放文件.txt') -Value 'USER FILE' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $appRoot 'fixture-job.txt') -Value $job -Encoding UTF8 -NoNewline
    $files=@(Get-ChildItem -LiteralPath (Join-Path $job 'stage') -File | ForEach-Object { @{ Path=$_.Name; Size=$_.Length; Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
    $manifest=@{ Schema=1; Version='1.4.2'; PackageName='FilmEcho-1.4.2-win-x64.zip'; Size=1; Sha256=('0'*64); Files=$files }
    $payload=[Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 8 -Compress))
    $envelope=@{ Payload=[Convert]::ToBase64String($payload); Signature=[Convert]::ToBase64String($signer.SignData($payload,'SHA256')) } | ConvertTo-Json -Compress
    @{ Root=$appRoot; Envelope=$envelope; PublicKeyXml=$signer.ToXmlString($false); CurrentVersion='1.4.1'; ParentPid=0; ParentStarted=0; English=$false } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $job 'plan.json') -Encoding UTF8
    return @{ Root=$appRoot; Job=$job }
}
function WaitFile([string]$path) {
    $limit=[DateTime]::UtcNow.AddSeconds(15)
    while (!(Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $limit) { Start-Sleep -Milliseconds 100 }
    return (Test-Path -LiteralPath $path)
}
try {
    $applyCase=Prepare 'apply'; $appRoot=$applyCase.Root; $job=$applyCase.Job
    $oldHash=(Get-FileHash -LiteralPath (Join-Path $appRoot '英语跟读播放器.exe')).Hash
    $dataHash=(Get-FileHash -LiteralPath (Join-Path $appRoot 'data\用户记录.txt')).Hash
    $parent=Start-Process -FilePath (Join-Path $appRoot '英语跟读播放器.exe') -ArgumentList '--handoff' -WindowStyle Hidden -PassThru
    $parent.WaitForExit(5000) | Out-Null
    Check (WaitFile (Join-Path $job 'result.txt')) '独立更新器在父程序退出后完成替换'
    Check ((Get-Content -LiteralPath (Join-Path $job 'result.txt') -Raw).StartsWith('SUCCESS')) '实际更新器进程报告 SUCCESS'
    Check ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $appRoot '英语跟读播放器.exe')).FileVersion -eq '1.4.2.0') '目标 EXE 实际从 1.4.1 升至 1.4.2'
    Check (WaitFile (Join-Path $appRoot 'new-started.txt')) '替换完成后自动启动新版本'
    Check ((Get-FileHash -LiteralPath (Join-Path $job 'backup\英语跟读播放器.exe')).Hash -eq $oldHash) '旧程序备份完整保留'
    Check ((Get-FileHash -LiteralPath (Join-Path $appRoot 'data\用户记录.txt')).Hash -eq $dataHash) '跨进程升级不改学习数据'
    Check ((Get-Content -LiteralPath (Join-Path $appRoot '用户自放文件.txt') -Raw).Trim() -eq 'USER FILE') '跨进程升级不改用户自放文件'
    Check (!(Test-Path -LiteralPath (Join-Path $appRoot '.updates\pending.json'))) '成功更新清除事务标记'

    $abortCase=Prepare 'abort'; $appRoot=$abortCase.Root; $job=$abortCase.Job
    $parent=Start-Process -FilePath (Join-Path $appRoot '英语跟读播放器.exe') -ArgumentList '--abort' -WindowStyle Hidden -PassThru
    $parent.WaitForExit(5000) | Out-Null
    $helperPid=[int](Get-Content -LiteralPath (Join-Path $appRoot 'helper-pid.txt'))
    $helper=Get-Process -Id $helperPid -ErrorAction SilentlyContinue
    if ($helper) { $helper.WaitForExit(5000) | Out-Null }
    Check ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $appRoot '英语跟读播放器.exe')).FileVersion -eq '1.4.1.0') '取消授权后实际更新器保持旧 EXE'
    Check (!(Test-Path -LiteralPath (Join-Path $appRoot 'new-started.txt')) -and !(Test-Path -LiteralPath (Join-Path $appRoot '.updates\pending.json'))) '取消后不安装、不启动新版'

    $recoverCase=Prepare 'recover'; $appRoot=$recoverCase.Root; $job=$recoverCase.Job
    New-Item -ItemType Directory -Path (Join-Path $job 'backup') | Out-Null
    Copy-Item -LiteralPath (Join-Path $appRoot '使用说明.md') -Destination (Join-Path $job 'backup\使用说明.md')
    Set-Content -LiteralPath (Join-Path $appRoot '使用说明.md') -Value 'PARTIAL REPLACEMENT' -Encoding UTF8
    @{ Job=$job; State='Applying'; Changed=@('使用说明.md'); Existed=@('使用说明.md') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $appRoot '.updates\pending.json') -Encoding UTF8
    $parent=Start-Process -FilePath (Join-Path $appRoot '英语跟读播放器.exe') -WindowStyle Hidden -PassThru
    $parent.WaitForExit(5000) | Out-Null
    Check (WaitFile (Join-Path $appRoot 'old-started.txt')) '启动入口发现中断事务后交给独立恢复进程，再重启旧版'
    Check ((Get-Content -LiteralPath (Join-Path $appRoot '使用说明.md') -Raw).Trim() -eq 'OLD DOC') '独立恢复进程实际恢复旧文件'
    Check (!(Test-Path -LiteralPath (Join-Path $appRoot '.updates\pending.json'))) '恢复完成清除中断标记'
} finally {
    $signer.Dispose()
    $report.Add('FAIL='+$failures)
    $report | Set-Content -LiteralPath (Join-Path $projectRoot 'verification\updater-process-results.txt') -Encoding UTF8
    $report
}
if ($failures -ne 0) { throw ('Updater verification failed: '+$failures) }
