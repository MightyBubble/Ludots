using System;
using Arch.Core;

namespace MassNavPlaygroundMod.Runtime;

public sealed class MassNavSimulationRuntime
{
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
    public MassNavAgentState AgentState { get; } = new();
    public MassNavFlowTuning FlowTuning { get; } = new();
    public MassNavFormationRuntime FormationRuntime { get; } = new();

    public int SelectedCount => _selectedEntities.Length;
    public uint SelectionRevision => _selectionRevision;
    public ReadOnlySpan<Entity> SelectedEntities => _selectedEntities;
    public int FrameIndex => _frameIndex;
    public int AgentsPerTeam { get; private set; } = 10_000;

    public void BeginFrame(float dt)
    {
        _frameIndex++;
        SelectionSnapshotCountFrame = 0;
        StructuralChangesFrame = 0;
        FlowReconcileCountFrame = 0;
        FrameMs = dt > 0f ? dt * 1000f : 0f;
        Fps = FrameMs > 0.001f ? 1000f / FrameMs : 0f;
    }

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
    }

    public void ClearSelection()
    {
        if (_selectedEntities.Length == 0)
        {
            return;
        }

        _selectedEntities = Array.Empty<Entity>();
        _selectionRevision++;
        SelectionSnapshotCountFrame++;
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
}
