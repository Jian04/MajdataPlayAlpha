$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$build = Join-Path $project '.build/Release'
$release = 'D:\Maimai\release\MajdataPlayAlpha'
$log = Join-Path $project '.codex-build/oct10-spin-build.log'
foreach ($marker in @('Build succeeded!', 'ALPHA CARRIER OPACITY PASS:', 'SPIN ORIGINAL IMAGE GPU PASS: note.png', 'SPIN ORIGINAL IMAGE GPU PASS: note_b.png')) {
    if (!(Select-String -LiteralPath $log -SimpleMatch $marker -Quiet)) { throw "Missing build check: $marker" }
}
if (!(Select-String -LiteralPath (Join-Path $project '.codex-build/oct10-spin-core.log') -SimpleMatch 'PASS: 1148 regression checks.' -Quiet)) { throw 'Parser regression did not pass.' }
if (!(Select-String -LiteralPath (Join-Path $project '.codex-build/oct10-spin-original.log') -SimpleMatch 'SPIN ORIGINAL VIEW MATCH PASS: 4304 notes' -Quiet)) { throw 'Original chart does not match View.' }
if (Get-Process -Name MajdataPlayAlpha -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($release + '\', [StringComparison]::OrdinalIgnoreCase) }) {
    throw 'The release Play is running. Close it before replacement.'
}
$backup = Join-Path $project ('.build/release-backup-spin-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$files = @()
foreach ($folderName in @('MajdataPlayAlpha_Data', 'MonoBleedingEdge', 'D3D12')) {
    $folder = Join-Path $build $folderName
    if (Test-Path -LiteralPath $folder) {
        $files += @(Get-ChildItem -LiteralPath $folder -File -Recurse | Where-Object { !$_.FullName.StartsWith((Join-Path $build 'MajdataPlayAlpha_Data/StreamingAssets')+'\',[StringComparison]::OrdinalIgnoreCase) })
    }
}
foreach ($fileName in @('MajdataPlayAlpha.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'dstorage.dll', 'dstoragecore.dll')) {
    $source = Join-Path $build $fileName
    if (Test-Path -LiteralPath $source) { $files += Get-Item -LiteralPath $source }
}
$changes = @()
foreach ($file in $files) {
    $relative = $file.FullName.Substring($build.Length + 1)
    $target = Join-Path $release $relative
    $hash = (Get-FileHash -LiteralPath $file.FullName).Hash
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $hash) { continue }
    if (Test-Path -LiteralPath $target) {
        $saved = Join-Path $backup $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null
        Copy-Item -LiteralPath $target -Destination $saved
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    if ((Get-FileHash -LiteralPath $target).Hash -ne $hash) { throw "Published hash differs: $relative" }
    $changes += @{ path=$relative; sha256=$hash }
}
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$infoPath = Join-Path $release 'ALPHA_BUILD_INFO.json'
$info = if (Test-Path -LiteralPath $infoPath) { Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
if (Test-Path -LiteralPath $infoPath) { Copy-Item -LiteralPath $infoPath -Destination (Join-Path $backup 'ALPHA_BUILD_INFO.json') }
$info['project'] = 'MajdataPlayAlpha'
$info['version'] = 'alpha.0.6.0'
$info['builtAt'] = (Get-Date).ToString('o')
$info['regressionChecks'] = 1148
$info['spinOriginalNotesMatched'] = 4304
$info['fixes'] = @($info['fixes']) + @('Overlay inline BPM/division preserves notes and timing controls as ViewAlpha; native main behavior unchanged', 'Inherited trajectory duration resolves at parsing on stream BPM and scales with playback speed', 'Original Spin Eternally PNG inheritance verified by real GPU pixels and separate main/overlay opacity')
$info | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $infoPath -Encoding utf8
$changes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backup 'published-files.json') -Encoding utf8
foreach ($name in @('MajdataPlayAlpha.exe', 'MajdataPlayAlpha_Data/Managed/Assembly-CSharp.dll', 'UnityPlayer.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $build $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $release $name)).Hash) { throw "Published hash differs: $name" }
}
Write-Output "Published $($changes.Count) changed files to $release (alpha.0.6.0); verified hashes; rollback backup: $backup"
