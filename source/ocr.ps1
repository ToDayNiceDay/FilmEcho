param([Parameter(Mandatory=$true)][string]$ImagePath,[Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
Add-Type -AssemblyName System.Drawing
[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
[Windows.Globalization.Language,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.SoftwareBitmap,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
$taskMethod=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
function AwaitWin($operation,$type) {
    $task=$taskMethod.MakeGenericMethod($type).Invoke($null,@($operation))
    $task.Wait()
    $task.Result
}
$original=[Drawing.Bitmap]::FromFile($ImagePath)
try {
    $cropRect=[Drawing.Rectangle]::new(0,[int]($original.Height*0.64),$original.Width,[int]($original.Height*0.36))
    $crop=$original.Clone($cropRect,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $cropPath=$ImagePath+'.crop.png'
    try { $crop.Save($cropPath,[Drawing.Imaging.ImageFormat]::Png) } finally { $crop.Dispose() }
} finally { $original.Dispose() }
$file=AwaitWin ([Windows.Storage.StorageFile]::GetFileFromPathAsync($cropPath)) ([Windows.Storage.StorageFile])
$stream=AwaitWin ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
try {
    $decoder=AwaitWin ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap=AwaitWin ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    try {
        $engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new('en-US'))
        if(!$engine) { $engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages() }
        if(!$engine) { throw 'Windows OCR language pack is unavailable' }
        $result=AwaitWin ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
        $text=($result.Lines | ForEach-Object { $_.Text }) -join "`r`n"
        [IO.File]::WriteAllText($OutputPath,$text,[Text.UTF8Encoding]::new($false))
    } finally { $bitmap.Dispose() }
} finally { $stream.Dispose() }
