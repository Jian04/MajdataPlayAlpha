$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$build = Join-Path $project '.build/Release'
$release = 'D:\Maimai\release\MajdataPlayAlpha'
$log = Join-Path $project 'Temp/unity-noise-default-final-build.log'
foreach ($marker in @('Build succeeded!', 'ALPHA NOISE WARNING CHECK PASS:', 'ALPHA NOISE FILTER CHECK PASS:', 'ALPHA DISPLAY SWITCH CHECK PASS:')) {
    if (!(Select-String -LiteralPath $log -SimpleMatch $marker -Quiet)) { throw "Missing build check: $marker" }
}
if (!(Select-String -LiteralPath (Join-Path $project 'Temp/alpha-noise-default-regression.log') -SimpleMatch 'PASS: 515 regression checks.' -Quiet)) { throw 'Parser regression did not pass.' }
if (!(Select-String -LiteralPath (Join-Path $project 'Temp/noise-default-judgment.log') -SimpleMatch 'PASS: 50 upstream judgment' -Quiet)) { throw 'Native judgment audit did not pass.' }
if (Get-Process -Name MajdataPlayAlpha -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($release + '\', [StringComparison]::OrdinalIgnoreCase) }) {
    throw 'The release Play is running. Save and close it before replacement.'
}
New-Item -ItemType Directory -Path $release -Force | Out-Null
foreach ($folderName in @('MajdataPlayAlpha_Data', 'MonoBleedingEdge', 'D3D12')) {
    $folder = Join-Path $build $folderName
    if (!(Test-Path -LiteralPath $folder)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $folder -File -Recurse) {
        $target = Join-Path $release $file.FullName.Substring($build.Length + 1)
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}
foreach ($fileName in @('MajdataPlayAlpha.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'dstorage.dll', 'dstoragecore.dll')) {
    $source = Join-Path $build $fileName
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $release $fileName) -Force }
}
foreach ($name in @('README.md', 'ALPHA_COMPATIBILITY.md')) {
    Copy-Item -LiteralPath (Join-Path $project $name) -Destination (Join-Path $release $name) -Force
}
$infoPath = Join-Path $release 'ALPHA_BUILD_INFO.json'
$info = if (Test-Path -LiteralPath $infoPath) { Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
$info['project'] = 'MajdataPlayAlpha'
$info['version'] = 'alpha.0.6.0'
$info['builtAt'] = (Get-Date).ToString('o')
$info['regressionChecks'] = 515
$info['unchangedJudgmentMethods'] = 50
$info['buildErrors'] = 0
$info['noiseWarningChecks'] = 'First-frame and prewarning-only URP pixels; warnings and active fields coexist; no prewarning input blocking.'
$info['fixes'] = @($info['fixes']) + @('Touch-speed and SV prewarning timing and full-window white flashes', 'Finish at the last active noise end; clip practice noise to its range', 'JUDGELINEEXPAND True/False, legacy syntax and seeking', 'Default restores configured Play display values and transitions')
$info | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $infoPath -Encoding utf8
foreach ($name in @('MajdataPlayAlpha.exe', 'MajdataPlayAlpha_Data/Managed/Assembly-CSharp.dll', 'MajdataPlayAlpha_Data/globalgamemanagers', 'UnityPlayer.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $build $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $release $name)).Hash) { throw "Published hash differs: $name" }
    Write-Output "Verified: $name"
}
Write-Output "Published noise and display fixes to $release (alpha.0.6.0)"
