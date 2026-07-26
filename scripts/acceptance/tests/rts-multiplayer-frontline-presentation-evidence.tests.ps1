Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Feature: Player screenshots show same-frame battlefield entities backed by gameplay evidence.

. (Join-Path $PSScriptRoot "..\run-rts-multiplayer-frontline-three-process.ps1") `
    -LoadPresentationEvidenceFunctionsOnly

$script:FixtureStateHash = "a" * 64

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

function New-ReceiptRequirement {
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

function New-WorldInstance {
    param(
        [Parameter(Mandatory = $true)][int]$OwnerStableId,
        [Parameter(Mandatory = $true)][int]$VisualStableId,
        [Parameter(Mandatory = $true)][string]$Template,
        [Parameter(Mandatory = $true)][int]$XCm,
        [Parameter(Mandatory = $true)][int]$YCm,
        [double]$ShortEdgePx = 20,
        [double]$AreaPx2 = 400
    )

    return [pscustomobject]@{
        ownerStableId = $OwnerStableId
        visualStableId = $VisualStableId
        templateId = $VisualStableId
        template = $Template
        worldXCm = $XCm
        worldYCm = $YCm
        screenLeftPx = 100
        screenTopPx = 100
        screenRightPx = 100 + $ShortEdgePx
        screenBottomPx = 100 + $ShortEdgePx
        shortEdgePx = $ShortEdgePx
        areaPx2 = $AreaPx2
    }
}

function Invoke-ReceiptFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string[]]$ReceiptLines,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Requirements,
        [bool]$IncludeCompletion = $true,
        [string]$CompletionFile = "frontline_001_advancing.png",
        [string]$CompletionEvidenceFile = "frontline_001_advancing.evidence.json",
        [double]$SidecarShortEdgePx = 20,
        [double]$SidecarAreaPx2 = 400
    )

    $diagnosticPath = Join-Path $Directory "raylib-diagnostic.log"
    $screenshotPath = Join-Path $Directory "frontline_001_advancing.png"
    $evidencePath = Join-Path $Directory "frontline_001_advancing.evidence.json"
    $lines = @(
        "[2026-01-01T00:00:00.0000000Z] screenshot milestone=advancing milestoneOrder=4 milestoneRevision=5 frame=600 cameraPos=(0,0,0) cameraTarget=(0,0,0)"
        "[2026-01-01T00:00:00.0000000Z] timing frame=600 visibleEntities=9 performerActive=9 primitiveRaw=20 primInstances=20 primBatches=10"
        "[2026-01-01T00:00:00.0000000Z] prefab-visual-counts lastFrame(mesh=20,decal=0,vfx=0,surface=0)"
    ) + $ReceiptLines
    if ($IncludeCompletion) {
        $lines += "[2026-01-01T00:00:00.0000000Z] screenshot-complete milestone=advancing milestoneOrder=4 milestoneRevision=5 frame=600 file=$CompletionFile evidence=$CompletionEvidenceFile"
    }
    [System.IO.File]::WriteAllLines($diagnosticPath, $lines)
    $sidecar = [ordered]@{
        schemaVersion = 2
        milestone = "advancing"
        milestoneOrder = 4
        milestoneRevision = 5
        hostFrame = 600
        cameraTargetXCm = 14700
        cameraTargetYCm = 15000
        viewportWidthPx = 1280
        viewportHeightPx = 720
        instances = @(
            New-WorldInstance -OwnerStableId 101 -VisualStableId 1001 `
                -Template "rts.frontline.infantry.body" -XCm 12000 -YCm 15000 `
                -ShortEdgePx $SidecarShortEdgePx -AreaPx2 $SidecarAreaPx2
        )
    }
    [System.IO.File]::WriteAllText(
        $evidencePath,
        ($sidecar | ConvertTo-Json -Depth 20),
        [System.Text.UTF8Encoding]::new($false))

    $capture = [pscustomobject]@{
        ProcessName = "fixture-client"
        DiagnosticPath = $diagnosticPath
        Milestones = @("advancing")
        Files = @(
            [pscustomobject]@{
                Milestone = "advancing"
                Path = $screenshotPath
                EvidencePath = $evidencePath
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
            throw "Expected failure containing '$ExpectedMessage', got '$($_.Exception.Message)'. Stack: $($_.ScriptStackTrace)"
        }
        return
    }
    throw "Expected fixture to fail with '$ExpectedMessage', but it passed."
}

function New-GameplayItem {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][int]$SeatSlot,
        [Parameter(Mandatory = $true)][int]$SelectedStableId,
        [Parameter(Mandatory = $true)][int]$AttackTargetStableId
    )

    return [pscustomobject]@{
        Name = $Name
        Value = [pscustomobject]@{
            role = "replicatedClient"
            seatSlot = $SeatSlot
            gameplay = [pscustomobject]@{
                winningSideIndex = 0
                meetingPoint = [pscustomobject]@{ xCm = $(if ($SeatSlot -eq 0) { 14700 } else { 15300 }); yCm = 15000 }
                siegePoint = [pscustomobject]@{ xCm = $(if ($SeatSlot -eq 0) { 20300 } else { 9700 }); yCm = 15000 }
                defeatedCoreLastPosition = [pscustomobject]@{
                    presentationStableId = 900
                    xCm = 23000
                    yCm = 15000
                }
                moveStartPositions = @(
                    [pscustomobject]@{
                        handle = "$SeatSlot`:1"
                        presentationStableId = $SelectedStableId
                        xCm = $(if ($SeatSlot -eq 0) { 9000 } else { 21000 })
                        yCm = 15000
                    }
                )
                attackTargetPositionBefore = [pscustomobject]@{
                    handle = "target"
                    presentationStableId = $AttackTargetStableId
                    xCm = $(if ($SeatSlot -eq 0) { 23000 } else { 14700 })
                    yCm = 15000
                }
                attackTargetHealthBefore = 100
                attackTargetHealthAfter = 80
                completedCameraTarget = [pscustomobject]@{ xCm = 23000; yCm = 15000 }
                completedLosingCoreCount = 0
                completedWinnerInfantryNearDefeatedCoreCount = 1
                completedWinnerInfantryNearDefeatedCorePositions = @(
                    [pscustomobject]@{
                        handle = "winner"
                        presentationStableId = 101
                        xCm = 22500
                        yCm = 15000
                    }
                )
                completedPresentationFrameId = 650
            }
        }
    }
}

function New-PresentationItem {
    param(
        [Parameter(Mandatory = $true)][string]$ProcessName,
        [Parameter(Mandatory = $true)][string]$Milestone,
        [Parameter(Mandatory = $true)][int]$CameraXCm,
        [Parameter(Mandatory = $true)][int]$CameraYCm,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Instances,
        [int]$HostFrame = 700
    )

    return [ordered]@{
        process = $ProcessName
        milestones = @(
            [ordered]@{
                milestone = $Milestone
                worldEvidence = [pscustomobject]@{
                    schemaVersion = 2
                    milestone = $Milestone
                    milestoneOrder = 1
                    milestoneRevision = 1
                    hostFrame = $HostFrame
                    cameraTargetXCm = $CameraXCm
                    cameraTargetYCm = $CameraYCm
                    viewportWidthPx = 1280
                    viewportHeightPx = 720
                    instances = @($Instances)
                }
            }
        )
    }
}

function New-AdvancingRule {
    return [pscustomobject]@{
        milestone = "advancing"
        perspective = "all"
        anchor = "meeting"
        positionToleranceCm = 7000
        cameraToleranceCm = 100
        requiredRoles = @(
            [pscustomobject]@{
                role = "selectedInfantry"
                template = "rts.frontline.infantry.body"
                source = "selectedInfantry"
                minimumOnscreen = 1
                minimumShortEdgePx = 14
                minimumAreaPx2 = 220
            }
        )
        forbiddenRoles = @()
        stableEntityMotion = [pscustomobject]@{
            template = "rts.frontline.infantry.body"
            minimumObservedMoveCm = 1000
        }
    }
}

function New-CompletedRule {
    return [pscustomobject]@{
        milestone = "completed"
        perspective = "all"
        anchor = "defeatedCore"
        positionToleranceCm = 2200
        cameraToleranceCm = 25
        requiredRoles = @(
            [pscustomobject]@{
                role = "winningInfantryWitness"
                template = "rts.frontline.infantry.body"
                source = "completedWinnerInfantry"
                minimumOnscreen = 1
                minimumShortEdgePx = 14
                minimumAreaPx2 = 220
            }
        )
        forbiddenRoles = @(
            [pscustomobject]@{
                role = "defeatedCore"
                template = "rts.frontline.core.base"
                scope = "screen"
            }
        )
        requireCompletedWorldState = $true
    }
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ludots-rts-world-evidence-tests-" + [Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
try {
    # Scenario: The screenshot, completion record, and sidecar describe the same milestone frame.
    $capture = New-ClientScreenshotCapture -ProcessName "client-a" -ArtifactDirectory $fixtureRoot -Configuration ([pscustomobject]@{
        path = "screens/client-a/frontline.png"
        milestones = @("ready", "advancing", "engaging", "completed")
    })
    if ($capture.EnvironmentVariables.Contains("LUDOTS_TAKE_SCREENSHOT_FRAMES") -or
        [string]$capture.EnvironmentVariables.LUDOTS_TAKE_SCREENSHOT_MILESTONES -cne "ready,advancing,engaging,completed" -or
        [System.IO.Path]::GetFileName([string]$capture.Files[0].EvidencePath) -cne "frontline_001_ready.evidence.json") {
        throw "Milestone capture did not bind screenshots and sidecars without a frame fallback."
    }

    $passDirectory = Join-Path $fixtureRoot "receipt-pass"
    [System.IO.Directory]::CreateDirectory($passDirectory) | Out-Null
    $passEvidence = Invoke-ReceiptFixture -Directory $passDirectory -ReceiptLines @(
        "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=rts.frontline.infantry.body templateId=4 submitted=2 onscreen=2 minShortEdgePx=18.50 minAreaPx2=320.25 stateSha256=$script:FixtureStateHash"
    ) -Requirements @(New-ReceiptRequirement -Role "infantry" -Template "rts.frontline.infantry.body")
    if ([int]$passEvidence.milestones[0].worldEvidence.instances[0].ownerStableId -ne 101 -or
        [int]$passEvidence.milestones[0].hostFrame -ne 600) {
        throw "Same-frame receipt fixture did not preserve its owner identity and host frame."
    }

    $forgedBoundsDirectory = Join-Path $fixtureRoot "forged-bounds"
    [System.IO.Directory]::CreateDirectory($forgedBoundsDirectory) | Out-Null
    Assert-FailsWith -ExpectedMessage "is not actually on screen" -Action {
        Invoke-ReceiptFixture -Directory $forgedBoundsDirectory -ReceiptLines @(
            "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=rts.frontline.infantry.body templateId=4 submitted=2 onscreen=2 minShortEdgePx=18.50 minAreaPx2=320.25 stateSha256=$script:FixtureStateHash"
        ) -Requirements @(New-ReceiptRequirement -Role "infantry" -Template "rts.frontline.infantry.body") `
            -SidecarAreaPx2 399
    }

    $missingCompletionDirectory = Join-Path $fixtureRoot "missing-completion"
    [System.IO.Directory]::CreateDirectory($missingCompletionDirectory) | Out-Null
    Assert-FailsWith -ExpectedMessage "has no completion diagnostic" -Action {
        Invoke-ReceiptFixture -Directory $missingCompletionDirectory -ReceiptLines @(
            "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=rts.frontline.infantry.body templateId=4 submitted=2 onscreen=2 minShortEdgePx=18.50 minAreaPx2=320.25 stateSha256=$script:FixtureStateHash"
        ) -Requirements @(New-ReceiptRequirement -Role "infantry" -Template "rts.frontline.infantry.body") `
            -IncludeCompletion $false
    }

    $wrongEvidenceDirectory = Join-Path $fixtureRoot "wrong-evidence-file"
    [System.IO.Directory]::CreateDirectory($wrongEvidenceDirectory) | Out-Null
    Assert-FailsWith -ExpectedMessage "names evidence" -Action {
        Invoke-ReceiptFixture -Directory $wrongEvidenceDirectory -ReceiptLines @(
            "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=rts.frontline.infantry.body templateId=4 submitted=2 onscreen=2 minShortEdgePx=18.50 minAreaPx2=320.25 stateSha256=$script:FixtureStateHash"
        ) -Requirements @(New-ReceiptRequirement -Role "infantry" -Template "rts.frontline.infantry.body") `
            -CompletionEvidenceFile "another-frame.evidence.json"
    }

    $gameplayItems = @(
        New-GameplayItem -Name "client-a" -SeatSlot 0 -SelectedStableId 101 -AttackTargetStableId 901
        New-GameplayItem -Name "client-b" -SeatSlot 1 -SelectedStableId 201 -AttackTargetStableId 301
    )

    # Scenario: HUD or pixel changes cannot pass when the stable unit did not move.
    Assert-DistinctClientMilestoneScreenshots -Screenshots @(
        [pscustomobject]@{ process = "client-a"; milestone = "advancing"; file = [pscustomobject]@{ sha256 = "hud-one" } }
        [pscustomobject]@{ process = "client-a"; milestone = "engaging"; file = [pscustomobject]@{ sha256 = "hud-two" } }
    )
    $notMoved = @(
        New-PresentationItem -ProcessName "client-a" -Milestone "advancing" -CameraXCm 14700 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 101 -VisualStableId 1001 -Template "rts.frontline.infantry.body" -XCm 9000 -YCm 15000
        )
        New-PresentationItem -ProcessName "client-b" -Milestone "advancing" -CameraXCm 15300 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 201 -VisualStableId 2001 -Template "rts.frontline.infantry.body" -XCm 21000 -YCm 15000
        )
    )
    Assert-FailsWith -ExpectedMessage "did not visibly move" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $notMoved -GameplayItems $gameplayItems `
            -Requirements @(New-AdvancingRule)
    }

    # Scenario: The correct template in the wrong battlefield region fails.
    $wrongRegionRule = [pscustomobject]@{
        milestone = "engaging"
        perspective = "loser"
        anchor = "meeting"
        positionToleranceCm = 500
        cameraToleranceCm = 100
        requiredRoles = @([pscustomobject]@{
            role = "defendingInfantry"; template = "rts.frontline.infantry.body"; source = "selectedInfantry"
            minimumOnscreen = 1; minimumShortEdgePx = 14; minimumAreaPx2 = 220
        })
        forbiddenRoles = @()
        requireObservedDamage = $true
    }
    $wrongRegion = @(
        New-PresentationItem -ProcessName "client-a" -Milestone "engaging" -CameraXCm 20300 -CameraYCm 15000 -Instances @()
        New-PresentationItem -ProcessName "client-b" -Milestone "engaging" -CameraXCm 15300 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 201 -VisualStableId 2001 -Template "rts.frontline.infantry.body" -XCm 21000 -YCm 15000
        )
    )
    Assert-FailsWith -ExpectedMessage "wrong 'meeting' region" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $wrongRegion -GameplayItems $gameplayItems `
            -Requirements @($wrongRegionRule)
    }

    $completedRule = New-CompletedRule
    $clientAComplete = New-PresentationItem -ProcessName "client-a" -Milestone "completed" `
        -CameraXCm 23000 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 101 -VisualStableId 1001 -Template "rts.frontline.infantry.body" -XCm 22500 -YCm 15000
        )
    $clientBComplete = New-PresentationItem -ProcessName "client-b" -Milestone "completed" `
        -CameraXCm 23000 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 101 -VisualStableId 1101 -Template "rts.frontline.infantry.body" -XCm 22500 -YCm 15000
        )

    # Scenario: A completed camera aimed away from the defeated core fails.
    $wrongCamera = @(
        $clientAComplete
        New-PresentationItem -ProcessName "client-b" -Milestone "completed" -CameraXCm 19000 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 101 -VisualStableId 1101 -Template "rts.frontline.infantry.body" -XCm 22500 -YCm 15000
        )
    )
    Assert-FailsWith -ExpectedMessage "camera is outside" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $wrongCamera -GameplayItems $gameplayItems `
            -Requirements @($completedRule)
    }

    # Scenario: A losing core still visible to either client fails.
    $coreStillVisible = @(
        $clientAComplete
        New-PresentationItem -ProcessName "client-b" -Milestone "completed" -CameraXCm 23000 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 101 -VisualStableId 1101 -Template "rts.frontline.infantry.body" -XCm 22500 -YCm 15000
            New-WorldInstance -OwnerStableId 900 -VisualStableId 9001 -Template "rts.frontline.core.base" -XCm 23000 -YCm 15000 -ShortEdgePx 30 -AreaPx2 900
        )
    )
    Assert-FailsWith -ExpectedMessage "still shows forbidden role" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $coreStillVisible -GameplayItems $gameplayItems `
            -Requirements @($completedRule)
    }

    # Scenario: A missing core without infantry witnesses in the final region fails.
    $noInfantryWitness = @(
        $clientAComplete
        New-PresentationItem -ProcessName "client-b" -Milestone "completed" -CameraXCm 23000 -CameraYCm 15000 -Instances @()
    )
    Assert-FailsWith -ExpectedMessage "has no same-frame stable entity" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $noInfantryWitness -GameplayItems $gameplayItems `
            -Requirements @($completedRule)
    }

    # Scenario: A losing-side infantry cannot stand in for the winning witness recorded by gameplay.
    $wrongWinnerIdentity = @(
        $clientAComplete
        New-PresentationItem -ProcessName "client-b" -Milestone "completed" -CameraXCm 23000 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 201 -VisualStableId 2101 -Template "rts.frontline.infantry.body" -XCm 22500 -YCm 15000
        )
    )
    Assert-FailsWith -ExpectedMessage "has no same-frame stable entity" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $wrongWinnerIdentity -GameplayItems $gameplayItems `
            -Requirements @($completedRule)
    }

    # Scenario: One correct client cannot substitute for two correct client views.
    Assert-FailsWith -ExpectedMessage "camera is outside" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems @($clientAComplete, $wrongCamera[1]) `
            -GameplayItems $gameplayItems -Requirements @($completedRule)
    }
    $verified = @(Assert-ClientWorldPresentationEvidence `
        -PresentationItems @($clientAComplete, $clientBComplete) `
        -GameplayItems $gameplayItems -Requirements @($completedRule))
    if ($verified.Count -ne 2 -or @($verified.process | Sort-Object -Unique).Count -ne 2) {
        throw "Passing world fixture did not prove both client views."
    }

    Write-Output "RTS Frontline same-frame world presentation evidence tests passed."
}
finally {
    if ([System.IO.Directory]::Exists($fixtureRoot)) {
        [System.IO.Directory]::Delete($fixtureRoot, $true)
    }
}
