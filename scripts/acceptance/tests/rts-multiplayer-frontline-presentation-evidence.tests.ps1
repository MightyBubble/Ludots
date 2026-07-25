Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "..\run-rts-multiplayer-frontline-three-process.ps1") `
    -LoadPresentationEvidenceFunctionsOnly

function New-Minimums {
    return [pscustomobject]@{
        minimumVisibleEntities = 1
        minimumActivePerformers = 1
        minimumAuthoredPrimitives = 1
        minimumSubmittedPrimitiveInstances = 1
        minimumSubmittedPrimitiveBatches = 1
        minimumPrefabVisuals = 1
    }
}

function New-Requirement {
    param(
        [Parameter(Mandatory = $true)][string]$Role,
        [Parameter(Mandatory = $true)][string]$Template,
        [int]$MinimumShortEdgePx = 14,
        [int]$MinimumAreaPx2 = 220
    )

    return [pscustomobject]@{
        role = $Role
        frames = @(600)
        template = $Template
        minimumSubmitted = 1
        minimumOnscreen = 1
        minimumShortEdgePx = $MinimumShortEdgePx
        minimumAreaPx2 = $MinimumAreaPx2
    }
}

function Invoke-Fixture {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string[]]$ReceiptLines,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Requirements
    )

    $diagnosticPath = Join-Path $Directory "raylib-diagnostic.log"
    $lines = @(
        "[2026-01-01T00:00:00.0000000Z] screenshot frame=600 cameraPos=(0,0,0) cameraTarget=(0,0,0)"
        "[2026-01-01T00:00:00.0000000Z] timing frame=600 visibleEntities=9 performerActive=9 primitiveRaw=20 primInstances=20 primBatches=10"
        "[2026-01-01T00:00:00.0000000Z] prefab-visual-counts lastFrame(mesh=20,decal=0,vfx=0,surface=0)"
    ) + $ReceiptLines
    [System.IO.File]::WriteAllLines($diagnosticPath, $lines)

    $capture = [pscustomobject]@{
        ProcessName = "fixture-client"
        DiagnosticPath = $diagnosticPath
        Frames = @(600)
    }
    return Read-ClientPresentationEvidence -Capture $capture -Minimums (New-Minimums) `
        -RequiredReceipts $Requirements
}

function Assert-FailsWith {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$ExpectedMessage
    )

    try {
        [void](& $Action)
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "Expected failure containing '$ExpectedMessage', got '$($_.Exception.Message)'."
        }
        return
    }

    throw "Expected fixture to fail with '$ExpectedMessage', but it passed."
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ludots-rts-receipt-tests-" + [Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
try {
    $passDirectory = Join-Path $fixtureRoot "pass"
    [System.IO.Directory]::CreateDirectory($passDirectory) | Out-Null
    $passEvidence = Invoke-Fixture -Directory $passDirectory -ReceiptLines @(
        "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=rts.frontline.infantry.body templateId=4 submitted=2 onscreen=2 minShortEdgePx=18.50 minAreaPx2=320.25"
    ) -Requirements @(
        New-Requirement -Role "infantry" -Template "rts.frontline.infantry.body"
    )
    if ($passEvidence.frames[0].presentationReceipts[0].role -cne "infantry") {
        throw "Passing fixture did not publish its role-specific receipt evidence."
    }

    $missingDirectory = Join-Path $fixtureRoot "missing-role"
    [System.IO.Directory]::CreateDirectory($missingDirectory) | Out-Null
    Assert-FailsWith -ExpectedMessage "has no submitted presentation receipt for role 'infantry'" -Action {
        Invoke-Fixture -Directory $missingDirectory -ReceiptLines @(
            "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=rts.frontline.core.base templateId=1 submitted=1 onscreen=1 minShortEdgePx=40.00 minAreaPx2=1200.00"
        ) -Requirements @(
            New-Requirement -Role "infantry" -Template "rts.frontline.infantry.body"
        )
    }

    $tinyDirectory = Join-Path $fixtureRoot "tiny-role"
    [System.IO.Directory]::CreateDirectory($tinyDirectory) | Out-Null
    Assert-FailsWith -ExpectedMessage "minimum short edge pixels" -Action {
        Invoke-Fixture -Directory $tinyDirectory -ReceiptLines @(
            "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=rts.frontline.infantry.body templateId=4 submitted=2 onscreen=2 minShortEdgePx=2.00 minAreaPx2=6.00"
        ) -Requirements @(
            New-Requirement -Role "infantry" -Template "rts.frontline.infantry.body"
        )
    }

    $overlayDirectory = Join-Path $fixtureRoot "overlay-only"
    [System.IO.Directory]::CreateDirectory($overlayDirectory) | Out-Null
    Assert-FailsWith -ExpectedMessage "has no submitted presentation receipt for role 'core'" -Action {
        Invoke-Fixture -Directory $overlayDirectory -ReceiptLines @(
            "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=selection.ground-ring templateId=9 submitted=8 onscreen=8 minShortEdgePx=80.00 minAreaPx2=6400.00"
        ) -Requirements @(
            New-Requirement -Role "core" -Template "rts.frontline.core.base" -MinimumShortEdgePx 24 -MinimumAreaPx2 700
        )
    }

    Write-Output "RTS Frontline presentation evidence tests passed."
}
finally {
    if ([System.IO.Directory]::Exists($fixtureRoot)) {
        [System.IO.Directory]::Delete($fixtureRoot, $true)
    }
}
