param(
    [Parameter(Mandatory=$true)][string]$UnityPath,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '../../.build/unity-check'),
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$editor = (Resolve-Path -LiteralPath $UnityPath).Path
if (!(Test-Path -LiteralPath (Join-Path (Split-Path -Parent $editor) 'Data/PlaybackEngines/iOSSupport'))) {
    throw '请先为这个 Unity Editor 安装 iOS Build Support。'
}
if (!$OutputPath) { $OutputPath = Join-Path $project '.codex-build/iOS' }
$output = [IO.Path]::GetFullPath($OutputPath)
$log = Join-Path $project '.codex-build/ios-export.log'
[IO.Directory]::CreateDirectory((Split-Path -Parent $log)) | Out-Null
$buildArgs = @('-batchmode','-quit','-projectPath',('"'+$project+'"'),'-buildTarget','iOS',
    '-executeMethod','MajdataPlay.Editor.AlphaMobileRelease.BuildIOS',
    '-alphaIOSOutput',('"'+$output+'"'),'-logFile',('"'+$log+'"'))
$build = Start-Process -FilePath $editor -ArgumentList $buildArgs -WindowStyle Hidden -PassThru
$build.WaitForExit()
if ($build.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $output 'Unity-iPhone.xcodeproj')) -or
    !(Select-String -LiteralPath $log -SimpleMatch 'ALPHA IOS XCODE EXPORTED:' -Quiet) -or
    (Select-String -LiteralPath $log -Pattern '^EntryPointNotFoundException:|^Rethrow as TypeInitializationException:' -Quiet)) {
    throw "iOS 工程导出失败，请查看 $log"
}
Write-Output "Xcode 工程：$output；还需在 Mac 上编译，当前不是 IPA。"
