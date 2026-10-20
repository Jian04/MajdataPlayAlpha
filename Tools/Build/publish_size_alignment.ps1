$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$build = Join-Path $project '.build/Release'
$release = 'D:\Maimai\release\MajdataPlayAlpha'
$log = Join-Path $project 'Temp/oct06-size-final-build.log'
foreach ($marker in @('Build succeeded!', 'ALPHA SIZE CHECK PASS:', 'ALPHA NOISE SIZE CHECK PASS:', 'ALPHA TOUCHSLIDE MINE CHECK PASS:', 'ALPHA NOISE WARNING CHECK PASS:', 'ALPHA NOISE FILTER CHECK PASS:')) {
    if (!(Select-String -LiteralPath $log -SimpleMatch $marker -Quiet)) { throw "Missing build check: $marker" }
}
if (!(Select-String -LiteralPath (Join-Path $project 'Temp/oct06-size-rprq-pq-regression.log') -SimpleMatch 'PASS: 807 regression checks.' -Quiet)) { throw 'Parser regression did not pass.' }
if (!(Select-String -LiteralPath (Join-Path $project 'Temp/oct06-size-judgment.log') -SimpleMatch 'PASS: 50 upstream judgment' -Quiet)) { throw 'Native judgment audit did not pass.' }
if (Get-Process -Name MajdataPlayAlpha -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($release + '\', [StringComparison]::OrdinalIgnoreCase) }) {
    throw 'The release Play is running. Save and close it before replacement.'
}
$backup = Join-Path $project ('.build/release-backup-size-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $release -Force | Out-Null
$files = @()
foreach ($folderName in @('MajdataPlayAlpha_Data', 'MonoBleedingEdge', 'D3D12')) {
    $folder = Join-Path $build $folderName
    if (Test-Path -LiteralPath $folder) { $files += @(Get-ChildItem -LiteralPath $folder -File -Recurse) }
}
foreach ($fileName in @('MajdataPlayAlpha.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'dstorage.dll', 'dstoragecore.dll')) {
    $source = Join-Path $build $fileName
    if (Test-Path -LiteralPath $source) { $files += Get-Item -LiteralPath $source }
}
# Replace only Unity player files. User charts, skins, scores and settings are outside this list.
foreach ($file in $files) {
    $relative = $file.FullName.Substring($build.Length + 1)
    $target = Join-Path $release $relative
    if (Test-Path -LiteralPath $target) {
        $saved = Join-Path $backup $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null
        Copy-Item -LiteralPath $target -Destination $saved
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
foreach ($name in @('README.md', 'ALPHA_COMPATIBILITY.md')) {
    $target = Join-Path $release $name
    if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup $name) }
    Copy-Item -LiteralPath (Join-Path $project $name) -Destination $target -Force
}
$infoPath = Join-Path $release 'ALPHA_BUILD_INFO.json'
$info = if (Test-Path -LiteralPath $infoPath) { Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
if (Test-Path -LiteralPath $infoPath) { Copy-Item -LiteralPath $infoPath -Destination (Join-Path $backup 'ALPHA_BUILD_INFO.json') }
$info['project'] = 'MajdataPlayAlpha'
$info['version'] = 'alpha.0.6.0'
$info['builtAt'] = (Get-Date).ToString('o')
$info['regressionChecks'] = 807
$info['unchangedJudgmentMethods'] = 50
$info['fakeRuntimeChecks'] = 280
$info['buildErrors'] = 0
$info['sizeChecks'] = 'Native local arrow pivots for Slide/ExtendSlide/Wifi; separate moving stars; complete Hold/Touch/TouchHold parts; fixed center; no accumulation/pool leakage; global and typed SIZEV/reset; noise warning and active mask dimensions with unchanged input.'
$info['fixes'] = @($info['fixes']) + @('SIZE uses ViewAlpha local transform dimensions without batched vertex drift', 'Global SIZEV overrides typed birth SIZE; Slide paths remain explicit', 'Noise SIZE/SIZEV scales warning and active visual regions around native sensor centers')
$info | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $infoPath -Encoding utf8
foreach ($name in @('MajdataPlayAlpha.exe', 'MajdataPlayAlpha_Data/Managed/Assembly-CSharp.dll', 'MajdataPlayAlpha_Data/globalgamemanagers', 'UnityPlayer.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $build $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $release $name)).Hash) { throw "Published hash differs: $name" }
    Write-Output "Verified: $name"
}
Write-Output "Published SIZE alignment to $release (alpha.0.6.0); rollback backup: $backup"
