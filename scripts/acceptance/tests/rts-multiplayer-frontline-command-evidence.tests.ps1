Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Feature: Command evidence preserves the formal admission contract for immediate and queued RTS orders.

. (Join-Path $PSScriptRoot "..\run-rts-multiplayer-frontline-three-process.ps1") `
    -LoadPresentationEvidenceFunctionsOnly

function New-Transition {
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$Result,
        [Parameter(Mandatory = $true)][int]$Tick
    )

    return [pscustomobject]@{
        stage = $Stage
        result = $Result
        admissionBatchIndex = 0
        observedInputRevision = $Tick
        observedCommittedTick = $Tick
        authoritativeCommittedTick = $Tick
    }
}

function New-Command {
    param(
        [Parameter(Mandatory = $true)][string]$Action,
        [Parameter(Mandatory = $true)][string]$ActorResult,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$History,
        [long]$IssuedInputRevision = 400,
        [int]$IssuedCommittedTick = 40
    )

    return [pscustomobject]@{
        action = $Action
        clientBatchSequence = 6
        issuedInputRevision = $IssuedInputRevision
        issuedCommittedTick = $IssuedCommittedTick
        actorCount = 1
        admissionStage = "Terminal"
        admissionResult = "TerminalCompleted"
        actorHandles = @("11:1")
        actorAdmissions = @([pscustomobject]@{
            batchIndex = 0
            stage = "EntityIntake"
            result = $ActorResult
        })
        admissionHistory = @($History)
    }
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

$queuedHistory = @(
    New-Transition -Stage "NetworkIntake" -Result "NetworkScheduled" -Tick 10
    New-Transition -Stage "GlobalIntake" -Result "Queued" -Tick 10
    New-Transition -Stage "EntityIntake" -Result "Queued" -Tick 11
    New-Transition -Stage "Terminal" -Result "TerminalCompleted" -Tick 30
)
$queued = New-Command -Action "QueueTrainInfantry" -ActorResult "Queued" -History $queuedHistory
[void](Assert-ClientCommandAdmissionEvidence `
    -ClientName "client-a" -Command $queued -ExpectedAction "QueueTrainInfantry" -ExpectedSequence 6)

$immediateHistory = @(
    New-Transition -Stage "NetworkIntake" -Result "NetworkScheduled" -Tick 10
    New-Transition -Stage "GlobalIntake" -Result "Queued" -Tick 10
    New-Transition -Stage "EntityIntake" -Result "Activated" -Tick 11
    New-Transition -Stage "Terminal" -Result "TerminalCompleted" -Tick 20
)
$immediate = New-Command -Action "TrainInfantry" -ActorResult "Activated" -History $immediateHistory
[void](Assert-ClientCommandAdmissionEvidence `
    -ClientName "client-a" -Command $immediate -ExpectedAction "TrainInfantry" -ExpectedSequence 6)

$wrongActor = New-Command -Action "QueueTrainInfantry" -ActorResult "Activated" -History $queuedHistory
Assert-FailsWith -ExpectedMessage "expected EntityIntake/Queued" -Action {
    Assert-ClientCommandAdmissionEvidence `
        -ClientName "client-a" -Command $wrongActor -ExpectedAction "QueueTrainInfantry" -ExpectedSequence 6
}

$forgedActivation = New-Command -Action "QueueTrainInfantry" -ActorResult "Queued" -History @(
    $queuedHistory[0]
    $queuedHistory[1]
    $queuedHistory[2]
    New-Transition -Stage "EntityIntake" -Result "Activated" -Tick 20
    $queuedHistory[3]
)
Assert-FailsWith -ExpectedMessage "must not contain EntityIntake/Activated" -Action {
    Assert-ClientCommandAdmissionEvidence `
        -ClientName "client-a" -Command $forgedActivation -ExpectedAction "QueueTrainInfantry" -ExpectedSequence 6
}

$missingTerminal = New-Command -Action "QueueTrainInfantry" -ActorResult "Queued" -History @(
    $queuedHistory[0]
    $queuedHistory[1]
    $queuedHistory[2]
)
Assert-FailsWith -ExpectedMessage "must contain exactly one scheduled, queued, and terminal transition" -Action {
    Assert-ClientCommandAdmissionEvidence `
        -ClientName "client-a" -Command $missingTerminal -ExpectedAction "QueueTrainInfantry" -ExpectedSequence 6
}

$duplicateTerminal = New-Command -Action "TrainInfantry" -ActorResult "Activated" -History @(
    $immediateHistory[0]
    $immediateHistory[1]
    $immediateHistory[2]
    $immediateHistory[3]
    New-Transition -Stage "Terminal" -Result "TerminalCompleted" -Tick 21
)
Assert-FailsWith -ExpectedMessage "must contain exactly one scheduled, activated, and terminal transition" -Action {
    Assert-ClientCommandAdmissionEvidence `
        -ClientName "client-a" -Command $duplicateTerminal -ExpectedAction "TrainInfantry" -ExpectedSequence 6
}

$barrierClients = @(
    [pscustomobject]@{
        Name = "client-a"
        Value = [pscustomobject]@{
            gameplay = [pscustomobject]@{ meetingBarrierCommittedTick = 30 }
            commands = @(New-Command -Action "AttackEnemyCore" -ActorResult "Activated" `
                -IssuedInputRevision 301 -IssuedCommittedTick 31 `
                -History @(
                    New-Transition -Stage "NetworkIntake" -Result "NetworkScheduled" -Tick 32
                    New-Transition -Stage "GlobalIntake" -Result "Queued" -Tick 32
                    New-Transition -Stage "EntityIntake" -Result "Activated" -Tick 33
                    New-Transition -Stage "Terminal" -Result "TerminalCompleted" -Tick 41
                ))
        }
    }
    [pscustomobject]@{
        Name = "client-b"
        Value = [pscustomobject]@{
            gameplay = [pscustomobject]@{ meetingBarrierCommittedTick = 50 }
            commands = @(New-Command -Action "AttackEnemyInfantry" -ActorResult "Activated" `
                -IssuedInputRevision 501 -IssuedCommittedTick 51 `
                -History @(
                    New-Transition -Stage "NetworkIntake" -Result "NetworkScheduled" -Tick 52
                    New-Transition -Stage "GlobalIntake" -Result "Queued" -Tick 52
                    New-Transition -Stage "EntityIntake" -Result "Activated" -Tick 53
                    New-Transition -Stage "Terminal" -Result "TerminalCompleted" -Tick 60
                ))
        }
    }
)
[void](Assert-MeetingBarrierCommandCausality -ClientItems $barrierClients)

$earlyAttackClients = @(
    $barrierClients[0]
    [pscustomobject]@{
        Name = "client-b"
        Value = [pscustomobject]@{
            gameplay = [pscustomobject]@{ meetingBarrierCommittedTick = 50 }
            commands = @(New-Command -Action "AttackEnemyInfantry" -ActorResult "Activated" `
                -IssuedInputRevision 490 -IssuedCommittedTick 49 `
                -History @(
                    New-Transition -Stage "NetworkIntake" -Result "NetworkScheduled" -Tick 52
                    New-Transition -Stage "GlobalIntake" -Result "Queued" -Tick 52
                    New-Transition -Stage "EntityIntake" -Result "Activated" -Tick 53
                    New-Transition -Stage "Terminal" -Result "TerminalCompleted" -Tick 60
                ))
        }
    }
)
Assert-FailsWith -ExpectedMessage "before its local meeting barrier" -Action {
    Assert-MeetingBarrierCommandCausality -ClientItems $earlyAttackClients
}

Write-Output "RTS Frontline command evidence tests passed."
