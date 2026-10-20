param([string]$Destination = 'D:\Maimai\release\MajdataPlayAlpha')
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$root = (Resolve-Path -LiteralPath $Destination).Path.TrimEnd('\')
$play = Test-Path -LiteralPath (Join-Path $root 'MajdataPlayAlpha.exe')
$view = Test-Path -LiteralPath (Join-Path $root 'App\MajdataView\MajdataView.exe')
if (!$play -and !$view) { throw '请选择 MajdataPlayAlpha 或 MajdataAlpha 发布文件夹。' }
$running = @(Get-Process -Name MajdataPlayAlpha,MajdataEdit,MajdataView,MajdataLauncher -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)
})
if ($running.Count) { throw '请先关闭该发布目录中的程序。' }
$archive = [IO.Path]::GetFullPath((Join-Path $project ('.codex-build\release-pruned\' + (Split-Path -Leaf $root) + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))))
if (!$archive.StartsWith($project + '\.codex-build\release-pruned\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid archive directory.' }
$candidates = [Collections.Generic.List[string]]::new()
if ($play) {
    foreach ($relative in @('ALPHA_BASELINE.json','ALPHA_BUILD_INFO.json','AndroidReleaseStatus.json','Cache','Logs','MajdataPlayAlpha_Data\StreamingAssets\MaiCharts\Original')) {
        $candidates.Add((Join-Path $root $relative))
    }
    $charts = Join-Path $root 'MaiCharts'
    if (Test-Path -LiteralPath $charts) {
        foreach ($directory in Get-ChildItem -LiteralPath $charts -Directory -Recurse -Force) {
            if ($directory.Name -in @('.majdata','.autosave') -or
                ($directory.Name -eq '.cache' -and $directory.Parent.Name -eq 'media')) {
                $candidates.Add($directory.FullName)
            }
        }
    }
    foreach ($skin in @('Skins','MajdataPlayAlpha_Data\StreamingAssets\Skins')) {
        $folder = Join-Path $root $skin
        if (Test-Path -LiteralPath $folder) {
            Get-ChildItem -LiteralPath $folder -Filter SOURCE.json -File -Recurse | ForEach-Object { $candidates.Add($_.FullName) }
        }
    }
}
if ($view) {
    foreach ($relative in @('App\MajdataEdit\Themes','App\MajdataView\MajdataView_Data\StreamingAssets\Background\SongDetailBg')) {
        $folder = Join-Path $root $relative
        if (Test-Path -LiteralPath $folder) {
            Get-ChildItem -LiteralPath $folder -Filter sources.json -File -Recurse | ForEach-Object { $candidates.Add($_.FullName) }
        }
    }
}
$removedBytes = 0L
$removedItems = [Collections.Generic.List[string]]::new()
foreach ($candidate in $candidates) {
    if (!(Test-Path -LiteralPath $candidate)) { continue }
    $source = (Resolve-Path -LiteralPath $candidate).Path
    if (!$source.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Candidate outside release directory.' }
    if ((Get-Item -LiteralPath $source -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked path: $source" }
    $relative = [IO.Path]::GetRelativePath($root, $source)
    $target = [IO.Path]::GetFullPath((Join-Path $archive $relative))
    if (!$target.StartsWith($archive + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Candidate outside archive directory.' }
    $bytes = (Get-ChildItem -LiteralPath $source -Recurse -File -Force | Measure-Object Length -Sum).Sum
    if (Test-Path -LiteralPath $source -PathType Leaf) { $bytes = (Get-Item -LiteralPath $source).Length }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    Move-Item -LiteralPath $source -Destination $target
    $removedBytes += $bytes
    $removedItems.Add($relative)
}
if ($removedItems.Count) { $removedItems | Set-Content -LiteralPath (Join-Path $archive 'removed-files.txt') -Encoding utf8 }
Write-Output ("已从发布目录移出 {0} 项，减少 {1:N2} MiB。" -f $removedItems.Count, ($removedBytes / 1MB))
Write-Output "归档：$archive"
