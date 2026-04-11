using System;
using Arch.Core;

namespace MassNavWebParityMod.Runtime;

public sealed class MassNavSimulationRuntime
{
    private const float TimingWeight = 0.18f;

    private readonly int[] _teamIds = new[] { 1, 2, 3, 4 };
    private Entity[] _selectionScratch = new Entity[256];
    private Entity[] _selectedEntities = Array.Empty<Entity>();
    private uint _selectionRevision;
    private bool _sceneResetRequested;
    private int _frameIndex;

    public int SelectionSnapshotCountFrame { get; private set; }
    public int StructuralChangesFrame { get; private set; }
    public int FlowReconcileCountFrame { get; private set; }
    public float FrameMs { get; private set; }
    public float Fps { get; private set; }
    public float SelectionSyncMs { get; private set; }
    public float FormationTargetMs { get; private set; }
    public float SimStepMs { get; private set; }
    public float HardResolveMs { get; private set; }
    public float EntitySyncMs { get; private set; }
    public float PrimitiveEmitMs { get; private set; }
    public MassNavAgentState AgentState { get; } = new();
    public MassNavFlowTuning FlowTuning { get; } = new();
    public MassNavFormationRuntime FormationRuntime { get; } = new();
    public MassNavWebParitySimState WebParity { get; } = new();

    public int SelectedCount => _selectedEntities.Length;
    public uint SelectionRevision => _selectionRevision;
    public ReadOnlySpan<Entity> SelectedEntities => _selectedEntities;
    public ReadOnlySpan<int> TeamIds => _teamIds;
    public int TeamCount => _teamIds.Length;
    public int FrameIndex => _frameIndex;
    public int AgentsPerTeam { get; private set; } = 2_500;
    public int SelectedTeamId { get; private set; } = 1;
    public MassNavFormationMode FormationMode { get; private set; } = MassNavFormationMode.None;

    public void BeginFrame(float dt)
    {
        _frameIndex++;
        SelectionSnapshotCountFrame = 0;
        StructuralChangesFrame = 0;
        FlowReconcileCountFrame = 0;
        FrameMs = dt > 0f ? dt * 1000f : 0f;
        Fps = FrameMs > 0.001f ? 1000f / FrameMs : 0f;
    }

    public void ObserveSelectionSync(double sampleMs) => SelectionSyncMs = Smooth(SelectionSyncMs, (float)sampleMs);
    public void ObserveFormationTargets(double sampleMs) => FormationTargetMs = Smooth(FormationTargetMs, (float)sampleMs);
    public void ObserveSimStep(double sampleMs) => SimStepMs = Smooth(SimStepMs, (float)sampleMs);
    public void ObserveHardResolve(double sampleMs) => HardResolveMs = Smooth(HardResolveMs, (float)sampleMs);
    public void ObserveEntitySync(double sampleMs) => EntitySyncMs = Smooth(EntitySyncMs, (float)sampleMs);
    public void ObservePrimitiveEmit(double sampleMs) => PrimitiveEmitMs = Smooth(PrimitiveEmitMs, (float)sampleMs);

    public Span<Entity> EnsureSelectionScratch(int required)
    {
        if (required > _selectionScratch.Length)
        {
            int next = _selectionScratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _selectionScratch, next);
        }

        return _selectionScratch.AsSpan(0, required);
    }

    public void SetSelection(ReadOnlySpan<Entity> entities, uint revision)
    {
        if (_selectedEntities.Length != entities.Length)
        {
            _selectedEntities = new Entity[entities.Length];
        }

        entities.CopyTo(_selectedEntities);
        _selectionRevision = revision;
        SelectionSnapshotCountFrame++;
        WebParity.SetSelectedFlags(AgentState, _selectedEntities);
    }

    public void ClearSelection()
    {
        if (_selectedEntities.Length == 0)
        {
            WebParity.SetSelectedFlags(AgentState, ReadOnlySpan<Entity>.Empty);
            return;
        }

        _selectedEntities = Array.Empty<Entity>();
        _selectionRevision++;
        SelectionSnapshotCountFrame++;
        WebParity.SetSelectedFlags(AgentState, ReadOnlySpan<Entity>.Empty);
    }

    public void MarkStructuralChange()
    {
        StructuralChangesFrame++;
    }

    public void MarkFlowReconcile()
    {
        FlowReconcileCountFrame++;
    }

    public void SetAgentsPerTeam(int agentsPerTeam)
    {
        int next = Math.Max(0, agentsPerTeam);
        if (AgentsPerTeam == next)
        {
            return;
        }

        AgentsPerTeam = next;
        RequestSceneReset();
    }

    public void SetSelectedTeam(int teamId)
    {
        if (Array.IndexOf(_teamIds, teamId) >= 0)
        {
            SelectedTeamId = teamId;
        }
    }

    public void CycleSelectedTeam()
    {
        int index = Array.IndexOf(_teamIds, SelectedTeamId);
        if (index < 0)
        {
            SelectedTeamId = _teamIds.Length > 0 ? _teamIds[0] : 0;
            return;
        }

        SelectedTeamId = _teamIds[(index + 1) % _teamIds.Length];
    }

    public void SetFormationMode(MassNavFormationMode mode)
    {
        FormationMode = mode;
    }

    public void RequestSceneReset()
    {
        _sceneResetRequested = true;
    }

    public bool ConsumeSceneResetRequest()
    {
        if (!_sceneResetRequested)
        {
            return false;
        }

        _sceneResetRequested = false;
        return true;
    }

    private static float Smooth(float current, float sampleMs)
    {
        if (sampleMs < 0f)
        {
            sampleMs = 0f;
        }

        return current <= 0.001f
            ? sampleMs
            : (current * (1f - TimingWeight)) + (sampleMs * TimingWeight);
    }
}
