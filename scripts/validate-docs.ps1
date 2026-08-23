[CmdletBinding()]
param(
    [string[]]$Paths = @()
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Add-Finding {
    param(
        [System.Collections.Generic.List[object]]$Collection,
        [string]$Rule,
        [string]$Source,
        [string]$Detail
    )

    $Collection.Add([PSCustomObject]@{
        Rule = $Rule
        Source = $Source
        Detail = $Detail
    }) | Out-Null
}

function Resolve-RepoTarget {
    param(
        [string]$RepoRoot,
        [string]$SourceFile,
        [string]$Target
    )

    if ([string]::IsNullOrWhiteSpace($Target)) {
        return $null
    }

    $normalizedTarget = $Target.Split('#')[0].Split('?')[0]
    if ([string]::IsNullOrWhiteSpace($normalizedTarget)) {
        return $null
    }

    if ($normalizedTarget -match '^(.*\.[A-Za-z0-9]+):(\d+(:\d+)?(-\d+(:\d+)?)?)$') {
        $normalizedTarget = $Matches[1]
    }

    if ($normalizedTarget.Contains('*') -or
        $normalizedTarget.Contains('{') -or
        $normalizedTarget.Contains('}') -or
        $normalizedTarget.Contains('<') -or
        $normalizedTarget.Contains('>') -or
        $normalizedTarget.Contains('...')) {
        return $null
    }

    if ($normalizedTarget -match '^(https?:|mailto:|file:|#)') {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($normalizedTarget)) {
        return $normalizedTarget
    }

    if ($normalizedTarget -match '^(gitbook|docs|src|assets|mods|scripts|skills|artifacts|external|\.github)/') {
        return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $normalizedTarget))
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $SourceFile) $normalizedTarget))
}

# 稀疏检出容错：Test-Path 失败时回退 git 树查询（blobless/sparse 克隆中
# 未物料化的路径在文件系统不存在，但可能真实存在于 HEAD 树中）。
function Test-RepoPath {
    param(
        [string]$RepoRoot,
        [string]$ResolvedPath
    )

    if (Test-Path $ResolvedPath) {
        return $true
    }

    $fullRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $fullPath = [System.IO.Path]::GetFullPath($ResolvedPath)
    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $relative = $fullPath.Substring($fullRoot.Length).TrimStart('\', '/').Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($relative)) {
        return $false
    }

    try {
        $output = & git -C $fullRoot ls-tree HEAD -- $relative 2>$null
        return (-not [string]::IsNullOrWhiteSpace(($output | Out-String)))
    } catch {
        return $false
    }
}

function Get-MarkdownLinks {
    param([string]$Content)

    return [regex]::Matches($Content, '\[[^\]]+\]\(([^)]+)\)')
}

function Get-BacktickPaths {
    param([string]$Content)

    return [regex]::Matches($Content, '(?<!`)`([^`\r\n]+)`(?!`)')
}

function Resolve-ScopedFiles {
    param(
        [string]$RepoRoot,
        [string[]]$Targets,
        [System.Collections.Generic.List[object]]$Findings
    )

    $scoped = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($target in $Targets) {
        if ([string]::IsNullOrWhiteSpace($target)) {
            continue
        }

        $resolvedTarget = if ([System.IO.Path]::IsPathRooted($target)) {
            [System.IO.Path]::GetFullPath($target)
        } else {
            [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $target))
        }

        if (-not (Test-Path $resolvedTarget)) {
            Add-Finding -Collection $Findings -Rule 'missing-scope-target' -Source $target -Detail 'scoped validation target not found'
            continue
        }

        $item = Get-Item $resolvedTarget
        if ($item.PSIsContainer) {
            foreach ($file in Get-ChildItem -Path $resolvedTarget -Recurse -File -Filter '*.md') {
                $scoped.Add($file.FullName) | Out-Null
            }
        } else {
            $scoped.Add($item.FullName) | Out-Null
        }
    }

    return $scoped
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDir '..'))
$findings = New-Object 'System.Collections.Generic.List[object]'

$requiredEntries = @(
    '.gitbook.yaml',
    'gitbook/README.md',
    'gitbook/SUMMARY.md',
    'gitbook/contributing/README.md',
    'gitbook/architecture/README.md',
    'gitbook/reference/README.md',
    'docs/README.md',
    'docs/conventions/README.md',
    'docs/architecture/README.md',
    'docs/reference/README.md',
    'docs/audits/README.md',
    'docs/adr/README.md',
    'docs/rfcs/README.md'
)

$scopedMode = $Paths.Count -gt 0
$gitbookFiles = @()
$docsEntryFiles = @()
$entryFiles = @()
$markdownFilesForNaming = @()

if ($scopedMode) {
    $scopedFiles = Resolve-ScopedFiles -RepoRoot $repoRoot -Targets $Paths -Findings $findings
    $scopedItems = $scopedFiles | ForEach-Object { Get-Item $_ }

    $gitbookFiles = $scopedItems |
        Where-Object {
            $_.Extension -eq '.md' -and
            $_.FullName.StartsWith((Join-Path $repoRoot 'gitbook'), [System.StringComparison]::OrdinalIgnoreCase)
        }

    $docsEntryFiles = $scopedItems |
        Where-Object {
            $_.Extension -eq '.md' -and
            $_.FullName.StartsWith((Join-Path $repoRoot 'docs'), [System.StringComparison]::OrdinalIgnoreCase)
        }

    $entryFiles = $scopedItems |
        Where-Object {
            $_.Extension -ne '.md' -or
            (
                -not $_.FullName.StartsWith((Join-Path $repoRoot 'gitbook'), [System.StringComparison]::OrdinalIgnoreCase) -and
                -not $_.FullName.StartsWith((Join-Path $repoRoot 'docs'), [System.StringComparison]::OrdinalIgnoreCase)
            )
        } |
        ForEach-Object { $_.FullName }

    $markdownFilesForNaming = @($gitbookFiles) + @($docsEntryFiles)
} else {
    $gitbookRoot = Join-Path $repoRoot 'gitbook'
    if (Test-Path $gitbookRoot) {
        $gitbookFiles = Get-ChildItem -Path $gitbookRoot -Recurse -File -Filter '*.md'
    }

    $docsEntryFiles = @(
        'docs/README.md',
        'docs/conventions/README.md',
        'docs/conventions/04_documentation_governance.md',
        'docs/architecture/README.md',
        'docs/reference/README.md',
        'docs/audits/README.md',
        'docs/adr/README.md',
        'docs/rfcs/README.md'
    ) | ForEach-Object {
        $fullPath = Join-Path $repoRoot $_
        if (Test-Path $fullPath) {
            Get-Item $fullPath
        }
    }

    $entryFiles = @(
        'README.md',
        'README_CN.md',
        'AGENTS.md',
        'CLAUDE.md',
        '.github/PULL_REQUEST_TEMPLATE.md'
    ) | ForEach-Object {
        Join-Path $repoRoot $_
    } | Where-Object { Test-Path $_ }

    foreach ($required in $requiredEntries) {
        $fullPath = Join-Path $repoRoot $required
        if (-not (Test-Path $fullPath)) {
            Add-Finding -Collection $findings -Rule 'missing-readme' -Source $required -Detail 'required documentation entry is missing'
        }
    }

    $markdownFilesForNaming = @($gitbookFiles) + @($docsEntryFiles)
}

$filesToValidate = @(
    $gitbookFiles | ForEach-Object { $_.FullName }
) + @(
    $docsEntryFiles | ForEach-Object { $_.FullName }
) + @(
    $entryFiles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

foreach ($file in $filesToValidate) {
    $relativeSource = $file.Replace($repoRoot + [System.IO.Path]::DirectorySeparatorChar, '').Replace('\', '/')
    $content = Get-Content -Raw -Encoding UTF8 $file
    if ($null -eq $content) {
        $content = ''
    }

    $allowLegacyMentions = $relativeSource -like 'docs/adr/*'
    if (-not $allowLegacyMentions) {
        foreach ($legacy in @('docs/developer-guide/', 'docs/arch-guide/')) {
            if ($content.Contains($legacy)) {
                Add-Finding -Collection $findings -Rule 'legacy-path' -Source $relativeSource -Detail "contains legacy path '$legacy'"
            }
        }
    }

    foreach ($match in Get-MarkdownLinks -Content $content) {
        $target = $match.Groups[1].Value.Trim()
        if ($target -match '^[A-Za-z]:\\') {
            Add-Finding -Collection $findings -Rule 'absolute-link' -Source $relativeSource -Detail "markdown link uses absolute local path '$target'"
            continue
        }

        $resolved = Resolve-RepoTarget -RepoRoot $repoRoot -SourceFile $file -Target $target
        if ($null -eq $resolved) {
            continue
        }

        if (-not (Test-RepoPath -RepoRoot $repoRoot -ResolvedPath $resolved)) {
            Add-Finding -Collection $findings -Rule 'missing-link-target' -Source $relativeSource -Detail "markdown link target not found: '$target'"
        }
    }

    foreach ($match in Get-BacktickPaths -Content $content) {
        $token = $match.Groups[1].Value.Trim()
        if ($token -notmatch '^(gitbook|docs|src|assets|mods|scripts|skills|artifacts|external|\.github)/') {
            continue
        }

        if ($token -match '^[A-Za-z]:\\') {
            Add-Finding -Collection $findings -Rule 'absolute-backtick-path' -Source $relativeSource -Detail "backtick path uses absolute local path '$token'"
            continue
        }

        $resolved = Resolve-RepoTarget -RepoRoot $repoRoot -SourceFile $file -Target $token
        if ($null -eq $resolved) {
            continue
        }

        $normalizedToken = $token.Split('#')[0].Split('?')[0]
        if ($normalizedToken -match '^(.*\.[A-Za-z0-9]+):(\d+(:\d+)?(-\d+(:\d+)?)?)$') {
            $normalizedToken = $Matches[1]
        }

        if (-not (Test-RepoPath -RepoRoot $repoRoot -ResolvedPath $resolved)) {
            if ([string]::IsNullOrWhiteSpace([System.IO.Path]::GetExtension($normalizedToken))) {
                continue
            }

            Add-Finding -Collection $findings -Rule 'missing-backtick-target' -Source $relativeSource -Detail "backtick path target not found: '$token'"
        }
    }
}

$namingRules = @(
    @{ Prefix = 'gitbook/'; Pattern = '^(README|SUMMARY|[a-z0-9_-]+)\.md$'; Rule = 'gitbook-name' },
    @{ Prefix = 'docs/conventions/'; Pattern = '^(README|\d\d_[a-z0-9_]+)\.md$'; Rule = 'conventions-name' },
    @{ Prefix = 'docs/architecture/'; Pattern = '^(README|[a-z0-9_]+)\.md$'; Rule = 'architecture-name' },
    @{ Prefix = 'docs/reference/'; Pattern = '^(README|[a-z0-9_]+)\.md$'; Rule = 'reference-name' },
    @{ Prefix = 'docs/audits/'; Pattern = '^(README|[a-z0-9_]+)\.md$'; Rule = 'audits-name' },
    @{ Prefix = 'docs/adr/'; Pattern = '^(README|ADR-\d{4}-[a-z0-9-]+)\.md$'; Rule = 'adr-name' },
    @{ Prefix = 'docs/rfcs/'; Pattern = '^(README|RFC-\d{4}-[a-z0-9-]+)\.md$'; Rule = 'rfcs-name' }
)

foreach ($markdownFile in $markdownFilesForNaming) {
    $relativeSource = $markdownFile.FullName.Replace($repoRoot + [System.IO.Path]::DirectorySeparatorChar, '').Replace('\', '/')
    $name = $markdownFile.Name

    foreach ($rule in $namingRules) {
        if ($relativeSource.StartsWith($rule.Prefix) -and ($name -notmatch $rule.Pattern)) {
            Add-Finding -Collection $findings -Rule $rule.Rule -Source $relativeSource -Detail "file name does not match pattern '$($rule.Pattern)'"
        }
    }
}

if ($findings.Count -gt 0) {
    Write-Host 'Documentation validation failed.' -ForegroundColor Red
    $findings | Sort-Object Rule, Source, Detail | Format-Table -AutoSize | Out-String | Write-Host
    exit 1
}

if ($scopedMode) {
    Write-Host 'Documentation validation passed (scoped).' -ForegroundColor Green
} else {
    Write-Host 'Documentation validation passed.' -ForegroundColor Green
}
