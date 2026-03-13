param(
    [string]$OutputJson = "",
    [string]$OutputCsv = "",
    [string]$OutputMarkdown = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$featuresRoot = Join-Path $repoRoot "docs\architecture\interaction\features"

if ([string]::IsNullOrWhiteSpace($OutputJson)) {
    $OutputJson = Join-Path $repoRoot "artifacts\interaction-feature-catalog.json"
}

if ([string]::IsNullOrWhiteSpace($OutputCsv)) {
    $OutputCsv = Join-Path $repoRoot "artifacts\interaction-feature-catalog.csv"
}

if ([string]::IsNullOrWhiteSpace($OutputMarkdown)) {
    $OutputMarkdown = Join-Path $repoRoot "artifacts\interaction-feature-catalog.md"
}

$outputDirs = @(
    (Split-Path -Parent $OutputJson),
    (Split-Path -Parent $OutputCsv),
    (Split-Path -Parent $OutputMarkdown)
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

foreach ($dir in $outputDirs) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

$chapterNames = [ordered]@{
    passive = "01_passive_abilities"
    instant_press = "02_instant_press"
    unit_target = "03_unit_target"
    point_target = "04_point_target"
    direction_skillshot = "05_direction_skillshot"
    charge_hold = "06_charge_hold_release"
    combo = "07_combo_multistage"
    response_window = "08_response_window_and_context"
    insertable_context = "08_response_window_and_context"
    mark_detonate = "08_response_window_and_context"
    context_scored = "09_context_scored"
    defense = "10_defense_parry"
    toggle_stance = "11_toggle_stance_transform"
    movement = "12_movement_abilities"
    placement = "13_placement_mark_channel"
    channel = "13_placement_mark_channel"
    finisher = "14_finisher_companion_special_resource_env"
    companion = "14_finisher_companion_special_resource_env"
    special_input = "14_finisher_companion_special_resource_env"
    resource = "14_finisher_companion_special_resource_env"
    environment = "14_finisher_companion_special_resource_env"
}

$culture = Get-Culture

$scenarioFiles = Get-ChildItem -Path $featuresRoot -Recurse -File -Filter *.md | Where-Object {
    $_.Directory.FullName -ne $featuresRoot -and
    $_.Directory.Name -ne "_common" -and
    $_.BaseName -match "^(?<code>[a-u]\d+)_(?<slug>.+)$"
}

$records = foreach ($file in $scenarioFiles) {
    $match = [regex]::Match($file.BaseName, "^(?<code>[a-u]\d+)_(?<slug>.+)$")
    if (-not $match.Success) {
        continue
    }

    $family = $file.Directory.Name
    $code = $match.Groups["code"].Value
    $slug = $match.Groups["slug"].Value
    $ordinal = [int]([regex]::Match($code, "\d+").Value)
    $title = $culture.TextInfo.ToTitleCase(($slug -replace "_", " "))
    $chapter = if ($chapterNames.Contains($family)) { $chapterNames[$family] } else { "unknown" }
    $chapterOrder = if ($chapter -match "^(?<order>\d{2})_") { [int]$Matches["order"] } else { 999 }
    $relativePath = $file.FullName.Substring($repoRoot.Length + 1).Replace("\", "/")

    [PSCustomObject]@{
        chapter = $chapter
        chapterOrder = $chapterOrder
        family = $family
        code = $code
        ordinal = $ordinal
        slug = $slug
        title = $title
        scenarioId = "$family.$($file.BaseName)"
        catalogKey = "interaction.$family.$code"
        relativePath = $relativePath
    }
}

$orderedRecords = $records | Sort-Object chapterOrder, family, ordinal, code
$familyCounts = $orderedRecords | Group-Object family | Sort-Object Name | ForEach-Object {
    [PSCustomObject]@{
        family = $_.Name
        count = $_.Count
        chapter = ($_.Group | Select-Object -First 1).chapter
    }
}

$catalog = [ordered]@{
    generatedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssK")
    root = "docs/architecture/interaction/features"
    totalScenarioDocs = $orderedRecords.Count
    families = $familyCounts
    scenarios = $orderedRecords
}

$catalog | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputJson -Encoding UTF8
$orderedRecords | Export-Csv -Path $OutputCsv -NoTypeInformation -Encoding UTF8

$markdown = New-Object System.Text.StringBuilder
$mdTick = [char]96
[void]$markdown.AppendLine("# Interaction Feature Catalog")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- generated: $mdTick$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')$mdTick")
[void]$markdown.AppendLine("- root: ${mdTick}docs/architecture/interaction/features${mdTick}")
[void]$markdown.AppendLine("- total leaf scenarios: $mdTick$($orderedRecords.Count)$mdTick")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Families")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("| Family | Chapter | Count |")
[void]$markdown.AppendLine("| --- | --- | ---: |")
foreach ($entry in $familyCounts) {
    [void]$markdown.AppendLine("| $($entry.family) | $($entry.chapter) | $($entry.count) |")
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Sample Entries")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("| Catalog Key | Scenario Id | Title | Path |")
[void]$markdown.AppendLine("| --- | --- | --- | --- |")
foreach ($entry in ($orderedRecords | Select-Object -First 24)) {
    [void]$markdown.AppendLine("| $($entry.catalogKey) | $($entry.scenarioId) | $($entry.title) | ${mdTick}$($entry.relativePath)${mdTick} |")
}
$markdown.ToString() | Set-Content -Path $OutputMarkdown -Encoding UTF8

Write-Host "Interaction catalog generated."
Write-Host "  JSON: $OutputJson"
Write-Host "  CSV:  $OutputCsv"
Write-Host "  MD:   $OutputMarkdown"
Write-Host "  Leaf scenarios: $($orderedRecords.Count)"
