param(
    [Parameter(Mandatory=$true)][string]$UnityPath,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '../..'),
    [string]$OutputPath,
    [string]$ExportedProject
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$editor = (Resolve-Path -LiteralPath $UnityPath).Path
$android = Join-Path (Split-Path -Parent $editor) 'Data/PlaybackEngines/AndroidPlayer'
if (!(Test-Path -LiteralPath $android)) { throw '请在 Unity Hub 安装 Android Build Support、SDK/NDK 和 OpenJDK。' }
if (!$OutputPath) { $OutputPath = Join-Path $project '.codex-build/Android/MajdataPlayAlpha.apk' }
$output = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($output) -ne '.apk') { throw '输出文件必须是 .apk。' }
$logDirectory = Join-Path $project '.codex-build'
[IO.Directory]::CreateDirectory($logDirectory) | Out-Null

if (!$ExportedProject) {
    $ExportedProject = Join-Path $project '.codex-build/Android/Gradle'
    $log = Join-Path $logDirectory 'android-export.log'
    $buildArgs = @('-batchmode','-quit','-projectPath',('"'+$project+'"'),'-buildTarget','Android',
        '-executeMethod','MajdataPlay.Editor.AlphaMobileRelease.ExportAndroidProject',
        '-alphaAndroidExport',('"'+$ExportedProject+'"'),'-logFile',('"'+$log+'"'))
    $build = Start-Process -FilePath $editor -ArgumentList $buildArgs -WindowStyle Hidden -PassThru
    $build.WaitForExit()
    if ($build.ExitCode -ne 0 -or
        !(Select-String -LiteralPath $log -SimpleMatch 'ALPHA ANDROID PROJECT EXPORTED:' -Quiet)) {
        throw "Android 工程导出失败，请查看 $log"
    }
}
$gradleProject = (Resolve-Path -LiteralPath $ExportedProject).Path
if (!(Test-Path -LiteralPath (Join-Path $gradleProject 'settings.gradle'))) { throw '缺少导出的 Gradle 工程。' }

# Upstream main.yml uses target SDK 29 for its standalone/FOSS APK, retaining legacy chart-folder access.
foreach ($relative in @('gradle.properties','launcher/build.gradle','unityLibrary/build.gradle')) {
    $path = Join-Path $gradleProject $relative
    $text = [IO.File]::ReadAllText($path)
    $text = [regex]::Replace($text, '(?m)^unity\.targetSdkVersion=.*$', 'unity.targetSdkVersion=29')
    $text = [regex]::Replace($text, '\b(targetSdk(?:Version)?\s+)\d+', ('$' + '{1}29'))
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}
$java = Join-Path $android 'OpenJDK/bin/java.exe'
$gradle = Join-Path $android 'Tools/gradle/lib/gradle-launcher-9.1.0.jar'
$gradleLog = Join-Path $logDirectory 'android-gradle-build.log'
$previousJava = $env:JAVA_HOME
try {
    $env:JAVA_HOME = Join-Path $android 'OpenJDK'
    & $java -classpath $gradle org.gradle.launcher.GradleMain -p $gradleProject assembleRelease --build-cache *> $gradleLog
    if ($LASTEXITCODE -ne 0) { throw "Gradle 构建失败，请查看 $gradleLog" }
}
finally { $env:JAVA_HOME = $previousJava }
$apks = @(Get-ChildItem -LiteralPath (Join-Path $gradleProject 'launcher/build/outputs/apk/release') -Filter '*.apk' -File)
if ($apks.Count -ne 1) { throw 'Gradle 没有生成唯一的 ARM64 APK。' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
Copy-Item -LiteralPath $apks[0].FullName -Destination $output -Force
Write-Output "APK 已生成：$output；target SDK 29，仍需实机验证。"
