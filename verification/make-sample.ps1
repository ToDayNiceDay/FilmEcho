$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& (Join-Path $root 'vendor\ffmpeg\ffmpeg.exe') -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=1280x720:rate=24' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -i (Join-Path $PSScriptRoot 'english.srt') -i (Join-Path $PSScriptRoot 'chinese.srt') -map 0:v -map 1:a -map 2:s -map 3:s -c:v libx264 -preset ultrafast -crf 24 -c:a aac -c:s srt -metadata:s:s:0 language=eng -metadata:s:s:0 title=English -metadata:s:s:1 language=chi -metadata:s:s:1 title=Chinese -t 30 (Join-Path $PSScriptRoot 'sample.mkv')
if($LASTEXITCODE -ne 0) { throw 'Sample generation failed' }
