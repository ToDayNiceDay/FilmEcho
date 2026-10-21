# FilmEcho 源码构建

源码包含两个构建分支：`-PublicRelease` 生成 1.4.3 发布版；不加此参数生成 1.4.2 完整开发版。发布版仅保留默认浏览器入口和地区地图。这里的步骤以发布版为主。

## 环境和编译

- Windows x64，.NET Framework 4.8。
- .NET SDK，使用其中的 Roslyn C# 编译器；本次源码验证环境为 SDK 7.0.203。
- Windows PowerShell。脚本当前按默认安装位置查找 `C:\Program Files\dotnet\sdk` 和 Windows Framework64 系统程序集。
- 需要处理词库或打包时，再安装 Python 3；生成合成测试片时才需要 FFmpeg。

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\source\build.ps1 -PublicRelease
```

会生成 `output/public/v1.4.3/英语跟读播放器.exe` 和 `FilmEcho.Updater.exe`。地图、图标和 OCR 脚本已随源码提供并嵌入主程序。该分支编译不需要 WebView2、mpv 或词库；播放及查词的运行依赖按下一节准备。

## 准备运行目录

第三方二进制、词库和个人数据没有提交到 Git。准备以下项目：

| 路径（相对仓库根目录） | 来源及用途 |
| --- | --- |
| `vendor/mpv/mpv.exe`、`vendor/mpv/d3dcompiler_43.dll` | 与 1.4.3 草稿配套的是 [mpv Windows 20261009](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261009) 的 x86_64 构建；源码和构建说明见上游及 `licenses` |
| `tools/ecdict.csv` | 从 [ECDICT](https://github.com/skywind3000/ECDICT) 项目获取 `ecdict.csv`，保留 MIT 声明 |
| `vendor/dictionary/ecdict.tsv.gz` | 由下面的词库转换命令生成 |

```powershell
New-Item -ItemType Directory -Force vendor\dictionary | Out-Null
python .\tools\prepare-dictionary.py
$app = '.\output\public\v1.4.3'
New-Item -ItemType Directory -Force "$app\vendor\mpv", "$app\vendor\dictionary", "$app\runtime" | Out-Null
Copy-Item .\vendor\mpv\mpv.exe, .\vendor\mpv\d3dcompiler_43.dll "$app\vendor\mpv"
Copy-Item .\vendor\dictionary\ecdict.tsv.gz "$app\vendor\dictionary"
Copy-Item .\source\input.conf "$app\runtime\input.conf"
Copy-Item .\update-source.example.json "$app\update-source.json"
& "$app\英语跟读播放器.exe"
```

示例更新配置为空，适合自行编译的程序；不配置官方自动更新，避免自定义构建被官方版本覆盖。学习记录会写入运行目录旁的 `data`。Windows OCR、语音和 SQLite 使用系统组件；OCR 和朗读还取决于已安装的语言及语音。

## 完整开发版

将 Microsoft.Web.WebView2 NuGet 包 `1.0.3650.58` 解压到 `tools/webview2`，确保有 `lib/net462` 与 `runtimes/win-x64/native`，然后运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\source\build.ps1
```

完整开发版输出到仓库根目录，包含内嵌网页和完整使用分布管理界面，运行时需要 WebView2 Runtime。不要把这一命令的结果当成 1.4.3 发布版。mpv、词库等运行依赖放在仓库的 `vendor` 目录。

## 验证

源码中包含 `*Verification.cs` 验证入口。发布版验证需要先完成运行目录准备。若要运行播放相关检查，将 FFmpeg 放在 `vendor/ffmpeg/ffmpeg.exe`，再生成合成测试片：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\verification\make-sample.ps1
$app = '.\output\public\v1.4.3'
New-Item -ItemType Directory -Force "$app\verification" | Out-Null
Copy-Item .\verification\sample.mkv "$app\verification\sample.mkv"
Start-Process "$app\英语跟读播放器.exe" -ArgumentList '--release-verify' -Wait
Start-Process "$app\英语跟读播放器.exe" -ArgumentList '--update-verify' -Wait
Get-Content "$app\verification\public-release-results.txt", "$app\verification\update-results.txt"
```

检查结果和截图保存在运行目录的 `verification`。运行检查前应设置一个可用的默认浏览器。验证代码使用隔离数据和可控网络响应；通过验证不代表真实网络升级或所有用户电脑均已验收。`tools/verify-updater.ps1` 还可单独验证更新器的跨进程替换及回滚。完整开发版的 `--real-verify`、`--ui-verify` 等历史测试包含 `E:\英语电影` 的本机路径假设，不作为通用发布检查。

## 打包与更新签名

发布版运行依赖准备好后，可运行 `python tools/package-public.py` 生成完整 ZIP 和更新 ZIP。运行包依然不包含 C# 源文件；源码从本仓库单独取得。`tools/package.py` 是完整开发版的旧打包脚本，依赖本机验证报告，不适用于刚克隆的仓库。

`tools/sign-public-update.ps1` 是官方维护者使用的签名工具，依赖本地 `output/private` 中受 Windows 当前用户保护的私钥。公开源码不包含这份密钥，也不包含 GitHub 登录配置。自行构建和试运行不需要执行签名脚本；派生发行版应使用自己的仓库、版本与签名信任配置。

项目自有代码沿用根目录 MIT 许可。公开本项目源码不替代第三方组件的源码分发要求；1.4.3 二进制 Release 当前仍为草稿，随包 mpv 的完整对应源码及构建材料尚待核对。仓库中的历史开发说明可供追溯，当前构建步骤以本文为准。
