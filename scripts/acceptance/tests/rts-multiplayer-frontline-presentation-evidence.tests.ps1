Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Feature: Players keep visible evidence from each requested battle milestone.
# Scenario: Given the player reaches advancing, when evidence is verified, then the image and completion record describe advancing.

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
        milestones = @("advancing")
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
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Requirements,
        [bool]$IncludeCompletion = $true,
        [string]$CompletionFile = "frontline_001_advancing.png"
    )

    $diagnosticPath = Join-Path $Directory "raylib-diagnostic.log"
    $lines = @(
        "[2026-01-01T00:00:00.0000000Z] screenshot milestone=advancing milestoneOrder=4 milestoneRevision=5 frame=600 cameraPos=(0,0,0) cameraTarget=(0,0,0)"
        "[2026-01-01T00:00:00.0000000Z] timing frame=600 visibleEntities=9 performerActive=9 primitiveRaw=20 primInstances=20 primBatches=10"
        "[2026-01-01T00:00:00.0000000Z] prefab-visual-counts lastFrame(mesh=20,decal=0,vfx=0,surface=0)"
    ) + $ReceiptLines
    if ($IncludeCompletion) {
        $lines += "[2026-01-01T00:00:00.0000000Z] screenshot-complete milestone=advancing milestoneOrder=4 milestoneRevision=5 frame=600 file=$CompletionFile"
    }
    [System.IO.File]::WriteAllLines($diagnosticPath, $lines)

    $capture = [pscustomobject]@{
        ProcessName = "fixture-client"
        DiagnosticPath = $diagnosticPath
        Milestones = @("advancing")
        Files = @(
            [pscustomobject]@{
                Milestone = "advancing"
                Path = (Join-Path $Directory "frontline_001_advancing.png")
            }
        )
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
    # Scenario: Given a player must show formation, battle and result, then the launcher requests those named milestones without frame fallback.
    $capture = New-ClientScreenshotCapture -ProcessName "client-a" -ArtifactDirectory $fixtureRoot -Configuration ([pscustomobject]@{
        path = "screens/client-a/frontline.png"
        milestones = @("advancing", "engaging", "completed")
    })
    if ($capture.EnvironmentVariables.Contains("LUDOTS_TAKE_SCREENSHOT_FRAMES") -or
        [string]$capture.EnvironmentVariables.LUDOTS_TAKE_SCREENSHOT_MILESTONES -cne "advancing,engaging,completed") {
        throw "Milestone capture configuration unexpectedly retained frame-based fallback."
    }
    if ([System.IO.Path]::GetFileName([string]$capture.Files[1].Path) -cne "frontline_002_engaging.png") {
        throw "Milestone capture configuration did not bind the battle image name to engaging."
    }
    Assert-FailsWith -ExpectedMessage "is duplicated" -Action {
        New-ClientScreenshotCapture -ProcessName "client-a" -ArtifactDirectory $fixtureRoot -Configuration ([pscustomobject]@{
            path = "screens/client-a/frontline.png"
            milestones = @("engaging", "engaging")
        })
    }
    Assert-FailsWith -ExpectedMessage "duplicates pixel evidence" -Action {
        Assert-DistinctClientMilestoneScreenshots -Screenshots @(
            [pscustomobject]@{ process = "client-a"; milestone = "advancing"; file = [pscustomobject]@{ sha256 = "same" } }
            [pscustomobject]@{ process = "client-a"; milestone = "engaging"; file = [pscustomobject]@{ sha256 = "same" } }
            [pscustomobject]@{ process = "client-a"; milestone = "completed"; file = [pscustomobject]@{ sha256 = "different" } }
        )
    }
    Assert-DistinctClientMilestoneScreenshots -Screenshots @(
        [pscustomobject]@{ process = "client-a"; milestone = "advancing"; file = [pscustomobject]@{ sha256 = "one" } }
        [pscustomobject]@{ process = "client-a"; milestone = "engaging"; file = [pscustomobject]@{ sha256 = "two" } }
        [pscustomobject]@{ process = "client-a"; milestone = "completed"; file = [pscustomobject]@{ sha256 = "three" } }
    )

    $passDirectory = Join-Path $fixtureRoot "pass"
    [System.IO.Directory]::CreateDirectory($passDirectory) | Out-Null
    $passEvidence = Invoke-Fixture -Directory $passDirectory -ReceiptLines @(
        "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=rts.frontline.infantry.body templateId=4 submitted=2 onscreen=2 minShortEdgePx=18.50 minAreaPx2=320.25"
    ) -Requirements @(
        New-Requirement -Role "infantry" -Template "rts.frontline.infantry.body"
    )
    if ($passEvidence.milestones[0].presentationReceipts[0].role -cne "infantry") {
        throw "Passing fixture did not publish its role-specific receipt evidence."
    }
    if ($passEvidence.milestones[0].milestone -cne "advancing" -or
        [int]$passEvidence.milestones[0].hostFrame -ne 600) {
        throw "Passing fixture did not bind the player's advancing view to its host diagnostic frame."
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

    # Scenario: Given the player reaches advancing, when no completion is published, then no later image may replace it.
    $missingCompletionDirectory = Join-Path $fixtureRoot "missing-completion"
    [System.IO.Directory]::CreateDirectory($missingCompletionDirectory) | Out-Null
    Assert-FailsWith -ExpectedMessage "has no completion diagnostic" -Action {
        Invoke-Fixture -Directory $missingCompletionDirectory -ReceiptLines @(
            "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=rts.frontline.infantry.body templateId=4 submitted=2 onscreen=2 minShortEdgePx=18.50 minAreaPx2=320.25"
        ) -Requirements @(
            New-Requirement -Role "infantry" -Template "rts.frontline.infantry.body"
        ) -IncludeCompletion $false
    }

    # Scenario: Given the player sees advancing, when the completion names another image, then acceptance rejects it.
    $wrongFileDirectory = Join-Path $fixtureRoot "wrong-file"
    [System.IO.Directory]::CreateDirectory($wrongFileDirectory) | Out-Null
    Assert-FailsWith -ExpectedMessage "instead of 'frontline_001_advancing.png'" -Action {
        Invoke-Fixture -Directory $wrongFileDirectory -ReceiptLines @(
            "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=rts.frontline.infantry.body templateId=4 submitted=2 onscreen=2 minShortEdgePx=18.50 minAreaPx2=320.25"
        ) -Requirements @(
            New-Requirement -Role "infantry" -Template "rts.frontline.infantry.body"
        ) -CompletionFile "frontline_002_engaging.png"
    }

    Write-Output "RTS Frontline presentation evidence tests passed."
}
finally {
    if ([System.IO.Directory]::Exists($fixtureRoot)) {
        [System.IO.Directory]::Delete($fixtureRoot, $true)
    }
}
