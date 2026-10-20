param([string]$Destination = 'D:\Maimai\release\MajdataPlayAlpha')
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$buildRoot = Join-Path $sourceRoot '.build/Release'
$buildLog = Join-Path $sourceRoot 'Temp/unity-alpha060-final-build.log'
if (!(Select-String -LiteralPath $buildLog -SimpleMatch 'Build succeeded!' -Quiet)) { throw 'Unity build has not succeeded.' }
if (!(Select-String -LiteralPath $buildLog -SimpleMatch 'ALPHA FAKE CHECK PASS:' -Quiet)) { throw 'Runtime checks have not passed.' }
if (!(Select-String -LiteralPath $buildLog -SimpleMatch 'ALPHA TOUCHSLIDE MINE CHECK PASS:' -Quiet)) { throw 'TouchSlide Mine checks have not passed.' }
if (!(Select-String -LiteralPath $buildLog -SimpleMatch 'ALPHA INPUT CHECK PASS: 131' -Quiet)) { throw 'D input checks have not passed.' }
if (!(Select-String -LiteralPath $buildLog -SimpleMatch 'ALPHA FEATURE CHECK PASS: 18' -Quiet)) { throw 'Expanded path checks have not passed.' }
if (!(Select-String -LiteralPath $buildLog -SimpleMatch 'ALPHA SV POOL CHECK PASS:' -Quiet)) { throw 'SV pool checks have not passed.' }
if (!(Select-String -LiteralPath $buildLog -SimpleMatch 'ALPHA REVERSE SV CHECK PASS:' -Quiet)) { throw 'Reverse SV rendering checks have not passed.' }
if (!(Select-String -LiteralPath $buildLog -SimpleMatch 'VIGNETTE GRADIENT CHECK PASS:' -Quiet)) { throw 'Vignette rendering checks have not passed.' }
if (!(Select-String -LiteralPath $buildLog -SimpleMatch 'ALPHA NOISE FILTER CHECK PASS:' -Quiet)) { throw 'Noise/filter runtime checks have not passed.' }
if (!(Select-String -LiteralPath $buildLog -SimpleMatch 'ALPHA EDIT TIMELINE CHECK PASS:' -Quiet)) { throw 'Edit timeline/skin checks have not passed.' }
if (Get-Process -Name 'MajdataPlayAlpha' -ErrorAction SilentlyContinue) { throw 'MajdataPlayAlpha is running; program files cannot be replaced.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$releaseRoot = (Resolve-Path -LiteralPath $Destination).Path
# Copy only player files. The release's songs, skins, settings and scores are independent.
foreach ($name in @('MajdataPlayAlpha_Data', 'MonoBleedingEdge', 'D3D12')) {
    $folder = Join-Path $buildRoot $name
    if (!(Test-Path -LiteralPath $folder)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $folder -File -Recurse) {
        $relative = $file.FullName.Substring($buildRoot.Length + 1)
        $target = Join-Path $releaseRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}
foreach ($name in @('MajdataPlayAlpha.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'dstorage.dll', 'dstoragecore.dll')) {
    $file = Join-Path $buildRoot $name
    if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination (Join-Path $releaseRoot $name) -Force }
}
foreach ($name in @('README.md', 'LICENSE', 'ALPHA_BASELINE.json')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination (Join-Path $releaseRoot $name) -Force
}
$info = [ordered]@{
    project='MajdataPlayAlpha'; version='alpha.0.6.1'; upstreamTag='2.0.3'
    upstreamCommit='637bba02f2963b9fc67fa9fb18e86ced6bc07561'; buildUnity='6000.6.0f1'
    builtAt=(Get-Date).ToString('o'); regressionChecks=467; unchangedJudgmentMethods=50
    fakeRuntimeChecks=([int]([regex]::Match((Get-Content -LiteralPath $buildLog -Raw),'ALPHA FAKE CHECK PASS: (\d+)').Groups[1].Value)); alphaPathRuntimeChecks=18; dInputRuntimeChecks=131; commandCatalogChecks=53; officialMineSkins=38
    defaultSkin='Deluxe'; touchSlideMineChecks='native safe result, forced sensor contact including safe-boundary frame, separate timed visual trail and unchanged sensor queue'
    officialMineCommit='e8c0cd301f52847e5d7436c0575984742c646933'; buildErrors=0
    fixes=@('Fixed alpha.0.6.1 display/build version','All supplied skin sprites including Hold and judge art, same-pack companion fallbacks','Saved Edit media timeline: trim, offsets, mixed tracks, fades and PV clips','Overdead compound TouchSlide Mine arcs preserved instead of stripped as commands','Executable and window icon match ViewAlpha','Alpha path arrows use native slide spacing','Alpha Mine sensor completion wins over same-frame safe result','TouchSlide Mine trail lifetime and visual/judge separation','Downloaded four-skin pack with native fallback sprites; default Deluxe','51-second TouchSlide Mine comparison fixture','10 new ViewAlpha visual filters','Phira-Pro noise regions with isolated center shrink','155-second complete syntax fixture','optional outward soft vignette','SV reversal pool exhaustion in 7wonders','dynamic D path sensors match ViewAlpha','native pseudo-each, non-C TouchHold and no-head behavior','camera orientation before future filters','global SV must not reveal slide tracks early',
        'ViewAlpha HS, bounce and spawn once/rewind display','visual-only trajectory inheritance: no judgment/count/score/audio',
        'per-note PNG/JPG skin inheritance','judge line tint, visibility and 22.5 degree expansion',
        'D heads: D sensor or either adjacent button, separate queue and feedback',
        'three-ray D Wifi with native sensor and mine judgment',
        'overlay stream state/BPM/HS isolation, comments and final slots',
        'combo semantic mapping, judge text, subtitles, audio and PV overlay')
}
$info | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $releaseRoot 'ALPHA_BUILD_INFO.json') -Encoding utf8
foreach ($name in @('MajdataPlayAlpha.exe', 'MajdataPlayAlpha_Data/Managed/Assembly-CSharp.dll', 'MajdataPlayAlpha_Data/globalgamemanagers')) {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $buildRoot $name)).Hash
    $releaseHash = (Get-FileHash -LiteralPath (Join-Path $releaseRoot $name)).Hash
    if ($sourceHash -ne $releaseHash) { throw "Published file differs: $name" }
    Write-Output "Verified: $name"
}
Write-Output "Published alpha.0.6.1 to $releaseRoot"

$demoSource = (Resolve-Path (Join-Path $sourceRoot '../output/Alpha_All_Syntax_Test')).Path
$demoDestination = Join-Path $releaseRoot 'MaiCharts/Alpha/Alpha_All_Syntax_Test'
foreach ($file in Get-ChildItem -LiteralPath $demoSource -File -Recurse | Where-Object { $_.Name -ne 'view.json' }) {
    $target = Join-Path $demoDestination $file.FullName.Substring($demoSource.Length + 1)
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
$docsRoot = $releaseRoot + '-docs'
New-Item -ItemType Directory -Path $docsRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceRoot 'Assets/Resources/NoiseZone/SOURCE.md') -Destination (Join-Path $docsRoot 'NoiseZoneSources.md') -Force
Write-Output 'Published Alpha syntax demo and noise source attribution.'

$touchDemoSource = (Resolve-Path (Join-Path $sourceRoot '../output/Alpha_TouchSlide_Mine_Test')).Path
$touchDemoDestination = Join-Path $releaseRoot 'MaiCharts/Alpha/Alpha_TouchSlide_Mine_Test'
foreach ($file in Get-ChildItem -LiteralPath $touchDemoSource -File) {
    New-Item -ItemType Directory -Path $touchDemoDestination -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $touchDemoDestination $file.Name) -Force
}
$skinSource = Join-Path $sourceRoot 'Assets/StreamingAssets/Skins'
$skinDestination = Join-Path $releaseRoot 'Skins'
foreach ($skin in Get-ChildItem -LiteralPath $skinSource -Directory) {
    if (!(Test-Path -LiteralPath (Join-Path $skin.FullName 'SOURCE.json'))) { continue }
    $installed = Join-Path $skinDestination $skin.Name
    if (!(Test-Path -LiteralPath $installed)) { continue }
    $installedRoot = (Resolve-Path -LiteralPath $installed).Path
    if (!$installedRoot.StartsWith($releaseRoot + '\Skins\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Skin target outside release directory.' }
    foreach ($old in Get-ChildItem -LiteralPath $installedRoot -Filter '*.png' -File -Recurse) {
        $relative = $old.FullName.Substring($installedRoot.Length + 1)
        if (!(Test-Path -LiteralPath (Join-Path $skin.FullName $relative))) { Remove-Item -LiteralPath $old.FullName }
    }
}
foreach ($file in Get-ChildItem -LiteralPath $skinSource -File -Recurse | Where-Object { $_.Extension -ne '.meta' }) {
    $target = Join-Path $skinDestination $file.FullName.Substring($skinSource.Length + 1)
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
Write-Output 'Published downloaded skin pack (default Deluxe) and TouchSlide Mine test.'

# Chart and score folders are user data. Only export bundled documentation.
$docsRoot = $releaseRoot + '-docs'
New-Item -ItemType Directory -Path $docsRoot -Force | Out-Null
foreach ($name in @('ALPHA_COMPATIBILITY.md','FILTER_CANDIDATES.md','MOBILE_RELEASE.md')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination (Join-Path $docsRoot $name) -Force
}
$documents = @(Get-ChildItem -LiteralPath $releaseRoot -File -Filter '*.md' | Where-Object { $_.Name -ne 'README.md' })
foreach ($folder in @('Skins','MajdataPlayAlpha_Data/StreamingAssets')) {
    $path = Join-Path $releaseRoot $folder
    if (Test-Path -LiteralPath $path) { $documents += @(Get-ChildItem -LiteralPath $path -File -Recurse -Filter '*.md') }
}
foreach ($document in $documents) {
    $relative = [IO.Path]::GetRelativePath($releaseRoot,$document.FullName)
    $target = Join-Path $docsRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $document.FullName -Destination $target -Force
    Remove-Item -LiteralPath $document.FullName
}
& (Join-Path $PSScriptRoot 'prune_release.ps1') -Destination $releaseRoot
