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
        [double]$AreaPx2 = 400,
        [double]$ScreenLeftPx = 100,
        [double]$ScreenTopPx = 100
    )

    return [pscustomobject]@{
        ownerStableId = $OwnerStableId
        visualStableId = $VisualStableId
        templateId = $VisualStableId
        template = $Template
        worldXCm = $XCm
        worldYCm = $YCm
        screenLeftPx = $ScreenLeftPx
        screenTopPx = $ScreenTopPx
        screenRightPx = $ScreenLeftPx + $ShortEdgePx
        screenBottomPx = $ScreenTopPx + $ShortEdgePx
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

function New-GroupMoveSourceGraphFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$MappingJson
    )

    $inputDirectory = Join-Path $Directory "assets\Input"
    [System.IO.Directory]::CreateDirectory($inputDirectory) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $inputDirectory "input_order_mappings.json"),
        $MappingJson,
        [System.Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{
        plannedMods = @([pscustomobject]@{ id = "RtsDemoMod"; rootPath = $Directory })
    }
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

function New-FramebufferRequirement {
    param(
        [Parameter(Mandatory = $true)][string]$Role,
        [Parameter(Mandatory = $true)][string]$Template,
        [Parameter(Mandatory = $true)][int]$Red,
        [Parameter(Mandatory = $true)][int]$Green,
        [Parameter(Mandatory = $true)][int]$Blue
    )

    return [pscustomobject]@{
        role = $Role
        milestones = @("ready")
        presentationTemplate = $Template
        acceptedColors = @([pscustomobject]@{ red = $Red; green = $Green; blue = $Blue })
        maximumChannelDifference = 0
        minimumPixelsPerInstance = 4
        minimumPassingInstances = 1
        regionMarginRatio = 0
    }
}

function New-FramebufferPresentationItem {
    $instances = @(
        [pscustomobject]@{
            templateId = 1; visualStableId = 101; template = "fixture.core"
            screenLeftPx = 1; screenTopPx = 1; screenRightPx = 4; screenBottomPx = 4
        }
        [pscustomobject]@{
            templateId = 2; visualStableId = 102; template = "fixture.harvester"
            screenLeftPx = 5; screenTopPx = 1; screenRightPx = 8; screenBottomPx = 4
        }
        [pscustomobject]@{
            templateId = 3; visualStableId = 103; template = "fixture.infantry"
            screenLeftPx = 1; screenTopPx = 5; screenRightPx = 4; screenBottomPx = 8
        }
        [pscustomobject]@{
            templateId = 4; visualStableId = 104; template = "fixture.crystal"
            screenLeftPx = 5; screenTopPx = 5; screenRightPx = 8; screenBottomPx = 8
        }
    )
    return [ordered]@{
        process = "fixture-client"
        milestones = @([ordered]@{
            milestone = "ready"
            worldEvidence = [pscustomobject]@{
                viewportWidthPx = 16
                viewportHeightPx = 16
                instances = $instances
            }
        })
    }
}

function Invoke-FramebufferFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$PngBase64,
        [Parameter(Mandatory = $true)][string]$DotnetPath,
        [Parameter(Mandatory = $true)][string]$LauncherAssemblyPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    [System.IO.Directory]::CreateDirectory($Directory) | Out-Null
    $screenshotPath = Join-Path $Directory "frontline_001_ready.png"
    [System.IO.File]::WriteAllBytes($screenshotPath, [Convert]::FromBase64String($PngBase64))
    $requirements = @(
        New-FramebufferRequirement -Role "core" -Template "fixture.core" -Red 31 -Green 41 -Blue 48
        New-FramebufferRequirement -Role "harvester" -Template "fixture.harvester" -Red 255 -Green 166 -Blue 31
        New-FramebufferRequirement -Role "infantry" -Template "fixture.infantry" -Red 209 -Green 224 -Blue 230
        New-FramebufferRequirement -Role "crystal" -Template "fixture.crystal" -Red 20 -Green 224 -Blue 255
    )
    return @(Read-ClientFramebufferPixelEvidence `
        -Screenshots @([pscustomobject]@{
            ProcessName = "fixture-client"
            Milestone = "ready"
            Path = $screenshotPath
        }) `
        -PresentationItems @(New-FramebufferPresentationItem) `
        -Requirements $requirements `
        -DotnetPath $DotnetPath `
        -LauncherAssemblyPath $LauncherAssemblyPath `
        -ArtifactDirectory $Directory `
        -WorkingDirectory $WorkingDirectory)
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ludots-rts-world-evidence-tests-" + [Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
try {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $rtsDemoRoot = Join-Path $repoRoot "mods\showcases\rts_demo\RtsDemoMod"
    $fixtureGroupMoveLayoutEvidence = Resolve-GroupMoveTargetLayoutEvidence -SourceGraph ([pscustomobject]@{
        plannedMods = @([pscustomobject]@{ id = "RtsDemoMod"; rootPath = $rtsDemoRoot })
    })
    $layoutSpacingCm = [int]$fixtureGroupMoveLayoutEvidence.spacingCm
    if ($layoutSpacingCm -le 0 -or
        [string]$fixtureGroupMoveLayoutEvidence.source -cne "groupMoveTargetLayout.spacingCm" -or
        [string]::IsNullOrWhiteSpace([string]$fixtureGroupMoveLayoutEvidence.config.sha256)) {
        throw "Formal group-move layout evidence did not preserve its positive spacing source and file hash."
    }

    $invalidLayoutRoot = Join-Path $fixtureRoot "invalid-layout"
    Assert-FailsWith -ExpectedMessage "must be Grid and contain moveTo exactly once" -Action {
        Resolve-GroupMoveTargetLayoutEvidence -SourceGraph (New-GroupMoveSourceGraphFixture `
            -Directory (Join-Path $invalidLayoutRoot "mode") `
            -MappingJson '{"groupMoveTargetLayout":{"mode":"Circle","spacingCm":140,"orderTypeKeys":["moveTo"]}}')
    }
    Assert-FailsWith -ExpectedMessage "must be Grid and contain moveTo exactly once" -Action {
        Resolve-GroupMoveTargetLayoutEvidence -SourceGraph (New-GroupMoveSourceGraphFixture `
            -Directory (Join-Path $invalidLayoutRoot "order") `
            -MappingJson '{"groupMoveTargetLayout":{"mode":"Grid","spacingCm":140,"orderTypeKeys":["attackTarget"]}}')
    }
    Assert-FailsWith -ExpectedMessage "must be a positive finite integer" -Action {
        Resolve-GroupMoveTargetLayoutEvidence -SourceGraph (New-GroupMoveSourceGraphFixture `
            -Directory (Join-Path $invalidLayoutRoot "spacing") `
            -MappingJson '{"groupMoveTargetLayout":{"mode":"Grid","spacingCm":0,"orderTypeKeys":["moveTo"]}}')
    }
    $dotnetPath = Get-DotnetCommand
    $launcherProject = Join-Path $repoRoot "src\Tools\Ludots.Launcher.Cli\Ludots.Launcher.Cli.csproj"
    $launcherAssemblyPath = Join-Path $repoRoot "src\Tools\Ludots.Launcher.Cli\bin\Release\net8.0\Ludots.Launcher.Cli.dll"
    [void](Invoke-NativeTextCommand -Name "build-framebuffer-evidence-cli" -FilePath $dotnetPath `
        -Arguments @("build", $launcherProject, "-c", "Release", "-m:1", "-nologo", "-clp:ErrorsOnly") `
        -WorkingDirectory $repoRoot)

    # Scenario: A terrain-and-HUD-only PNG fails even when the sidecar claims all entity boxes exist.
    $emptyFramebuffer = @(Invoke-FramebufferFixture -Directory (Join-Path $fixtureRoot "framebuffer-empty") `
        -PngBase64 "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAGUlEQVR4nGOouBT1nxLMMGrAqAGjBgwXAwDd1qMfVAGMlAAAAABJRU5ErkJggg==" `
        -DotnetPath $dotnetPath -LauncherAssemblyPath $launcherAssemblyPath -WorkingDirectory $repoRoot)
    Assert-FailsWith -ExpectedMessage "does not visibly contain every required role" -Action {
        Assert-ClientFramebufferPixelEvidencePassed -Items $emptyFramebuffer
    }

    # Scenario: The real PNG passes when every player-visible role color occupies its claimed instance box.
    $completeFramebuffer = @(Invoke-FramebufferFixture -Directory (Join-Path $fixtureRoot "framebuffer-complete") `
        -PngBase64 "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAOElEQVR4nGOouBT1nxLMACLkNQ3gGMT/v0wejoeIARSHwcUHz+AYxBd58B+Oh4gBFIfBqAFD3QAAxnqlBYGtsNkAAAAASUVORK5CYII=" `
        -DotnetPath $dotnetPath -LauncherAssemblyPath $launcherAssemblyPath -WorkingDirectory $repoRoot)
    Assert-ClientFramebufferPixelEvidencePassed -Items $completeFramebuffer
    if ($completeFramebuffer.Count -ne 1 -or
        @($completeFramebuffer[0].requirements | Where-Object { -not [bool]$_.passed }).Count -ne 0) {
        throw "Complete framebuffer fixture did not retain passing evidence for every required role: itemCount=$($completeFramebuffer.Count), failedRequirementCount=$(@($completeFramebuffer[0].requirements | Where-Object { -not [bool]$_.passed }).Count)."
    }

    # Scenario: A failed top-level inspector result cannot pass when every nested requirement claims success.
    Assert-FailsWith -ExpectedMessage "inspector reported passed=false" -Action {
        Assert-ClientFramebufferPixelEvidencePassed -Items @([pscustomobject]@{
            process = "fixture-client"
            milestone = "ready"
            passed = $false
            requirements = @([pscustomobject]@{
                role = "core"
                passed = $true
            })
        })
    }

    # Scenario: One missing required role fails even though the other three roles are real pixels.
    $missingRoleFramebuffer = @(Invoke-FramebufferFixture -Directory (Join-Path $fixtureRoot "framebuffer-missing-role") `
        -PngBase64 "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAMklEQVR4nGOouBT1nxLMACLkNQ3gGMT/v0wejoeIARSHwcUHz+B4iBpAcRiMGjDUDQAACmGiPr2E254AAAAASUVORK5CYII=" `
        -DotnetPath $dotnetPath -LauncherAssemblyPath $launcherAssemblyPath -WorkingDirectory $repoRoot)
    Assert-FailsWith -ExpectedMessage "role 'crystal'" -Action {
        Assert-ClientFramebufferPixelEvidencePassed -Items $missingRoleFramebuffer
    }

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
    $completion = Get-ScreenshotCompletionRecord -Target ([pscustomobject]@{
        ProcessName = "fixture-client"
        Milestone = "advancing"
        Path = (Join-Path $passDirectory "frontline_001_advancing.png")
        EvidencePath = (Join-Path $passDirectory "frontline_001_advancing.evidence.json")
        DiagnosticPath = (Join-Path $passDirectory "raylib-diagnostic.log")
    })
    if ($null -eq $completion -or [int]$completion.HostFrame -ne 600) {
        throw "Screenshot completion record was not parsed from the fixture diagnostic."
    }

    $emptyAdvancingDirectory = Join-Path $fixtureRoot "empty-advancing"
    [System.IO.Directory]::CreateDirectory($emptyAdvancingDirectory) | Out-Null
    Assert-FailsWith -ExpectedMessage "has 0 onscreen" -Action {
        Invoke-ReceiptFixture -Directory $emptyAdvancingDirectory -ReceiptLines @(
            "[2026-01-01T00:00:00.0000000Z] presentation-receipt template=rts.frontline.infantry.body templateId=4 submitted=2 onscreen=0 minShortEdgePx=0.00 minAreaPx2=0.00 stateSha256=$script:FixtureStateHash"
        ) -Requirements @(New-ReceiptRequirement -Role "frontlineInfantry" -Template "rts.frontline.infantry.body")
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
            -Requirements @(New-AdvancingRule) -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
    }

    $distinctLayoutRule = New-AdvancingRule
    $distinctLayoutRule | Add-Member -NotePropertyName distinctEntityLayout -NotePropertyValue ([pscustomobject]@{
        template = "rts.frontline.infantry.body"
        scope = "allVisibleTemplate"
        region = "screen"
        minimumInstances = 2
        minimumWorldSeparationSource = "groupMoveTargetLayout.spacingCm"
        maximumScreenOverlapRatio = 0.5
    })
    $worldOverlap = @(
        New-PresentationItem -ProcessName "client-a" -Milestone "advancing" -CameraXCm 14700 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 101 -VisualStableId 1001 -Template "rts.frontline.infantry.body" -XCm 12000 -YCm 15000 -ScreenLeftPx 100
            New-WorldInstance -OwnerStableId 102 -VisualStableId 1002 -Template "rts.frontline.infantry.body" -XCm (12000 + $layoutSpacingCm - 1) -YCm 15000 -ScreenLeftPx 140
        )
        New-PresentationItem -ProcessName "client-b" -Milestone "advancing" -CameraXCm 15300 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 201 -VisualStableId 2001 -Template "rts.frontline.infantry.body" -XCm 18000 -YCm 15000 -ScreenLeftPx 100
            New-WorldInstance -OwnerStableId 202 -VisualStableId 2002 -Template "rts.frontline.infantry.body" -XCm (18000 + $layoutSpacingCm) -YCm 15000 -ScreenLeftPx 140
        )
    )
    Assert-FailsWith -ExpectedMessage "overlaps 'rts.frontline.infantry.body' entities '101' and '102' in the world" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $worldOverlap -GameplayItems $gameplayItems `
            -Requirements @($distinctLayoutRule) -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
    }

    $screenOverlap = @(
        New-PresentationItem -ProcessName "client-a" -Milestone "advancing" -CameraXCm 14700 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 101 -VisualStableId 1001 -Template "rts.frontline.infantry.body" -XCm 12000 -YCm 15000
            New-WorldInstance -OwnerStableId 102 -VisualStableId 1002 -Template "rts.frontline.infantry.body" -XCm (12000 + $layoutSpacingCm) -YCm 15000 -ScreenLeftPx 102.4
        )
        New-PresentationItem -ProcessName "client-b" -Milestone "advancing" -CameraXCm 15300 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 201 -VisualStableId 2001 -Template "rts.frontline.infantry.body" -XCm 18000 -YCm 15000 -ScreenLeftPx 100
            New-WorldInstance -OwnerStableId 202 -VisualStableId 2002 -Template "rts.frontline.infantry.body" -XCm (18000 + $layoutSpacingCm) -YCm 15000 -ScreenLeftPx 140
        )
    )
    Assert-FailsWith -ExpectedMessage "overlaps 'rts.frontline.infantry.body' entities '101' and '102' on screen" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $screenOverlap -GameplayItems $gameplayItems `
            -Requirements @($distinctLayoutRule) -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
    }

    $emptyScreenBox = @(
        New-PresentationItem -ProcessName "client-a" -Milestone "advancing" -CameraXCm 14700 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 101 -VisualStableId 1001 -Template "rts.frontline.infantry.body" -XCm 12000 -YCm 15000
            New-WorldInstance -OwnerStableId 102 -VisualStableId 1002 -Template "rts.frontline.infantry.body" -XCm (12000 + $layoutSpacingCm) -YCm 15000 -ScreenLeftPx 140 -ShortEdgePx 0
        )
        New-PresentationItem -ProcessName "client-b" -Milestone "advancing" -CameraXCm 15300 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 201 -VisualStableId 2001 -Template "rts.frontline.infantry.body" -XCm 18000 -YCm 15000
            New-WorldInstance -OwnerStableId 202 -VisualStableId 2002 -Template "rts.frontline.infantry.body" -XCm (18000 + $layoutSpacingCm) -YCm 15000 -ScreenLeftPx 140
        )
    )
    Assert-FailsWith -ExpectedMessage "non-finite or empty screen box" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $emptyScreenBox -GameplayItems $gameplayItems `
            -Requirements @($distinctLayoutRule) -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
    }

    $nonFiniteScreenBox = @(
        New-PresentationItem -ProcessName "client-a" -Milestone "advancing" -CameraXCm 14700 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 101 -VisualStableId 1001 -Template "rts.frontline.infantry.body" -XCm 12000 -YCm 15000
            New-WorldInstance -OwnerStableId 102 -VisualStableId 1002 -Template "rts.frontline.infantry.body" -XCm (12000 + $layoutSpacingCm) -YCm 15000 -ScreenLeftPx ([double]::NaN)
        )
        New-PresentationItem -ProcessName "client-b" -Milestone "advancing" -CameraXCm 15300 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 201 -VisualStableId 2001 -Template "rts.frontline.infantry.body" -XCm 18000 -YCm 15000
            New-WorldInstance -OwnerStableId 202 -VisualStableId 2002 -Template "rts.frontline.infantry.body" -XCm (18000 + $layoutSpacingCm) -YCm 15000 -ScreenLeftPx 140
        )
    )
    Assert-FailsWith -ExpectedMessage "non-finite or empty screen box" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $nonFiniteScreenBox -GameplayItems $gameplayItems `
            -Requirements @($distinctLayoutRule) -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
    }

    $distinctLayout = @(
        New-PresentationItem -ProcessName "client-a" -Milestone "advancing" -CameraXCm 14700 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 101 -VisualStableId 1001 -Template "rts.frontline.infantry.body" -XCm 12000 -YCm 15000 -ScreenLeftPx 100
            New-WorldInstance -OwnerStableId 102 -VisualStableId 1002 -Template "rts.frontline.infantry.body" -XCm (12000 + $layoutSpacingCm) -YCm 15000 -ScreenLeftPx 140
        )
        New-PresentationItem -ProcessName "client-b" -Milestone "advancing" -CameraXCm 15300 -CameraYCm 15000 -Instances @(
            New-WorldInstance -OwnerStableId 201 -VisualStableId 2001 -Template "rts.frontline.infantry.body" -XCm 18000 -YCm 15000 -ScreenLeftPx 100
            New-WorldInstance -OwnerStableId 202 -VisualStableId 2002 -Template "rts.frontline.infantry.body" -XCm (18000 + $layoutSpacingCm) -YCm 15000 -ScreenLeftPx 140
        )
    )
    $distinctVerified = @(Assert-ClientWorldPresentationEvidence -PresentationItems $distinctLayout `
        -GameplayItems $gameplayItems -Requirements @($distinctLayoutRule) `
        -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence)
    if ($distinctVerified.Count -ne 2 -or
        @($distinctVerified | Where-Object { [int]$_.distinctEntityLayout.instanceCount -ne 2 }).Count -ne 0) {
        throw "Distinct infantry layout fixture did not preserve two independently visible entities per client."
    }

    $stableScopeRule = New-AdvancingRule
    $stableScopeRule | Add-Member -NotePropertyName distinctEntityLayout -NotePropertyValue ([pscustomobject]@{
        template = "rts.frontline.infantry.body"
        scope = "stableEntitySources"
        region = "screen"
        sources = @("selectedInfantry")
        minimumInstances = 2
        minimumWorldSeparationSource = "groupMoveTargetLayout.spacingCm"
        maximumScreenOverlapRatio = 0.5
    })
    Assert-FailsWith -ExpectedMessage "distinct layout requires at least 2" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $distinctLayout -GameplayItems $gameplayItems `
            -Requirements @($stableScopeRule) -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
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
            -Requirements @($wrongRegionRule) -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
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
            -Requirements @($completedRule) -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
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
            -Requirements @($completedRule) -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
    }

    # Scenario: A missing core without infantry witnesses in the final region fails.
    $noInfantryWitness = @(
        $clientAComplete
        New-PresentationItem -ProcessName "client-b" -Milestone "completed" -CameraXCm 23000 -CameraYCm 15000 -Instances @()
    )
    Assert-FailsWith -ExpectedMessage "has no same-frame stable entity" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems $noInfantryWitness -GameplayItems $gameplayItems `
            -Requirements @($completedRule) -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
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
            -Requirements @($completedRule) -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
    }

    # Scenario: One correct client cannot substitute for two correct client views.
    Assert-FailsWith -ExpectedMessage "camera is outside" -Action {
        Assert-ClientWorldPresentationEvidence -PresentationItems @($clientAComplete, $wrongCamera[1]) `
            -GameplayItems $gameplayItems -Requirements @($completedRule) `
            -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence
    }
    $verified = @(Assert-ClientWorldPresentationEvidence `
        -PresentationItems @($clientAComplete, $clientBComplete) `
        -GameplayItems $gameplayItems -Requirements @($completedRule) `
        -GroupMoveLayoutEvidence $fixtureGroupMoveLayoutEvidence)
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
