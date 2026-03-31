using System;
using System.Collections.Generic;
using Ludots.Core.Engine.TimeFlow;

namespace TimeflowShowcaseMod.Runtime;

internal sealed class TimeflowShowcaseScenarioState
{
    public required TimeflowScenarioId ScenarioId { get; init; }
    public required string Title { get; init; }
    public required string InspirationLine { get; init; }
    public required string Phase { get; set; }
    public required string StatusLine { get; set; }
    public required string ControlsLine { get; set; }
    public List<TimeflowActorState> Actors { get; } = new();
    public List<string> RecentEvents { get; } = new();
    public List<string> CommandQueue { get; } = new();
    public int FixedTick { get; set; }
    public int PresentationFrame { get; set; }
    public int PhaseFixedTicks { get; set; }
    public int PhasePresentationFrames { get; set; }
    public int SelectedIndex { get; set; }
    public int PopupCount { get; set; }
    public int MacroSpeedLevel { get; set; }
    public float BreakGauge { get; set; }
    public bool TerminalPaused { get; set; }
    public bool GlobalPauseActive { get; set; }
    public bool GlobalBulletActive { get; set; }
    public bool LocalPauseActive { get; set; }
    public bool PartyHasteActive { get; set; }
    public bool EnemySlowActive { get; set; }
    public bool FeverActive { get; set; }
    public bool PopupPauseActive { get; set; }
    public bool EventQueued { get; set; }
    public int FocusIndex { get; set; }
}

internal sealed class TimeflowActorState
{
    public TimeflowActorState(string name, int team, string domainName, float x, float y, float health, float speed)
    {
        Name = name;
        Team = team;
        DomainName = domainName;
        X = x;
        Y = y;
        TargetX = x;
        TargetY = y;
        Health = health;
        Speed = speed;
    }

    public string Name { get; }
    public int Team { get; }
    public string DomainName { get; }
    public float X { get; set; }
    public float Y { get; set; }
    public float TargetX { get; set; }
    public float TargetY { get; set; }
    public float Health { get; set; }
    public float Charge { get; set; }
    public float Energy { get; set; }
    public int WaitTicks { get; set; }
    public float Speed { get; }
    public int OrdersQueued { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class TimeflowShowcaseSnapshot
{
    public TimeflowScenarioId ScenarioId { get; init; }
    public string ScenarioTitle { get; init; } = string.Empty;
    public string InspirationLine { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string StatusLine { get; init; } = string.Empty;
    public string ControlsLine { get; init; } = string.Empty;
    public int FixedTick { get; init; }
    public int PresentationFrame { get; init; }
    public int UiRevision { get; init; }
    public int FocusIndex { get; init; }
    public bool GlobalPauseActive { get; init; }
    public bool GlobalBulletActive { get; init; }
    public bool LocalPauseActive { get; init; }
    public float BreakGauge { get; init; }
    public int MacroSpeedLevel { get; init; }
    public IReadOnlyList<TimeflowActorSnapshot> Actors { get; init; } = Array.Empty<TimeflowActorSnapshot>();
    public IReadOnlyList<string> RecentEvents { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CommandQueue { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TimeFlowDomainSnapshot> Domains { get; init; } = Array.Empty<TimeFlowDomainSnapshot>();
}

public sealed class TimeflowActorSnapshot
{
    public string Name { get; init; } = string.Empty;
    public int Team { get; init; }
    public string DomainName { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    public float Health { get; init; }
    public float Charge { get; init; }
    public float Energy { get; init; }
    public int WaitTicks { get; init; }
    public int OrdersQueued { get; init; }
    public string Status { get; init; } = string.Empty;
    public int EffectiveScalePermille { get; init; }
}
