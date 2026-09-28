# Live Riverhold capture, 2026-09-27: the map boss stood on Chapel subtile (7,12).
# Run: powershell -NoProfile -File Tests/Check-RiverholdBossArena.ps1
$ErrorActionPreference = 'Stop'
$capture = Get-Content (Join-Path $PSScriptRoot 'RadarBossArenaRiverhold.json') -Raw | ConvertFrom-Json
$entries = Get-Content (Join-Path $PSScriptRoot '../Plugins/Radar/boss_arena_tgt_files.txt') -Raw | ConvertFrom-Json
$arenas = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::Ordinal)
foreach ($entry in $entries.PSObject.Properties) { $arenas.Add($entry.Name, $entry.Value) }
# Both DrawTgtIcons and CollectEntityPaths select terrain with this exact-key lookup.
$targets = @($capture.tiles.PSObject.Properties | Where-Object { $arenas.ContainsKey($_.Name) })
if ($targets.Count -ne 1) { throw "Expected one Riverhold Boss Arena target, got $($targets.Count)." }
$position = $targets[0].Value[0]
for ($axis = 0; $axis -lt 2; $axis++) {
    $delta = $capture.bossGridPosition[$axis] - $position[$axis]
    if ($delta -lt 0 -or $delta -ge 23) { throw 'Selected tile does not contain the observed map boss.' }
}
Write-Output 'PASS: Riverhold selects one arena tile containing the observed boss, for both icon and path lookup.'
