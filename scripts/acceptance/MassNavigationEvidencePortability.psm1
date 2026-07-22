Set-StrictMode -Version Latest

function ConvertTo-PortableEvidenceLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Lines,
        [Parameter(Mandatory = $true)]
        [string]$RunDirectory,
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot,
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$RunName
    )

    foreach ($line in $Lines) {
        $portable = [string]$line
        $portable = $portable.Replace($RunDirectory, $RunName)
        $portable = $portable.Replace($OutputRoot, ".")
        $portable = $portable.Replace($RepoRoot, "<repo-root>")

        if ($portable.StartsWith("bootstrap=", [System.StringComparison]::Ordinal)) {
            $portable = "bootstrap=<launcher-bootstrap>"
        }
        elseif ($portable.StartsWith("recording=", [System.StringComparison]::Ordinal)) {
            $portable = "recording=$RunName"
        }
        elseif ($portable.StartsWith("summary=", [System.StringComparison]::Ordinal)) {
            $portable = "summary=$RunName/summary.json"
        }

        $portable
    }
}

function Get-EvidenceAbsolutePathViolations {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root)
    $textExtensions = @(
        ".csv",
        ".json",
        ".jsonl",
        ".log",
        ".md",
        ".mmd",
        ".txt",
        ".xml",
        ".yaml",
        ".yml"
    )
    $windowsRootedPath = '(?<![A-Za-z0-9])[A-Za-z]:[\\/]'
    $uncPath = '(^|[\s`"''(=])\\\\[^\\\s]+\\'
    $unixMachinePath = '(^|[\s`"''(=])/(home|Users|tmp|private/tmp|var/tmp|opt/hostedtoolcache|__w)(/|\\)'

    foreach ($file in Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse) {
        if ($file.Extension -notin $textExtensions) {
            continue
        }

        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName -Encoding UTF8) {
            $lineNumber++
            if ($line -match $windowsRootedPath -or $line -match $uncPath -or $line -match $unixMachinePath) {
                $rootPrefix = $resolvedRoot.TrimEnd([char[]]"\/")
                $relativePath = $file.FullName.Substring($rootPrefix.Length)
                $relativePath = $relativePath.TrimStart([char[]]"\/").Replace("\", "/")
                "$relativePath`:$lineNumber contains a machine-absolute path"
            }
        }
    }
}

Export-ModuleMember -Function ConvertTo-PortableEvidenceLog, Get-EvidenceAbsolutePathViolations
