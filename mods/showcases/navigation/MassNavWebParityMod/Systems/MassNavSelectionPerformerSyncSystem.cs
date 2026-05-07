using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using Ludots.Core.Engine;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavSelectionPerformerSyncSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;
    private bool[] _selectionCache = Array.Empty<bool>();
    private uint _lastSelectionRevision = uint.MaxValue;
    private int _lastStructuralRevision = -1;

    public MassNavSelectionPerformerSyncSystem(GameEngine engine, MassNavSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        if (_lastSelectionRevision == _simulation.SelectionRevision &&
            _lastStructuralRevision == _simulation.StructuralChangeRevision)
        {
            return;
        }

        PerformerCommandBuffer commands = _engine.GetService(CoreServiceKeys.PerformerCommandBuffer)
            ?? throw new InvalidOperationException("MassNavWebParityMod requires PerformerCommandBuffer for selection presentation.");
        int paramKey = _simulation.Config.Presentation.SelectionVisibilityParamKey;
        EnsureSelectionCache(_simulation.AgentState.ControllableCount);

        for (int i = 0; i < _simulation.AgentState.ControllableCount; i++)
        {
            Entity owner = _simulation.AgentState.ControllableAgents[i];
            bool selected = i < _simulation.WebParity.SelectedFlags.Length && _simulation.WebParity.SelectedFlags[i] != 0;
            bool force = _lastStructuralRevision != _simulation.StructuralChangeRevision;
            if (!force && _selectionCache[i] == selected)
            {
                continue;
            }

            _selectionCache[i] = selected;
            if (!_engine.World.IsAlive(owner) ||
                !_engine.World.Has<PresentationOwnerHasPerformerPayload>(owner))
            {
                continue;
            }

            ref readonly PresentationOwnerHasPerformerPayload payload = ref _engine.World.Get<PresentationOwnerHasPerformerPayload>(owner);
            if (payload.SingleRootPerformer == Entity.Null ||
                !_engine.World.IsAlive(payload.SingleRootPerformer))
            {
                continue;
            }

            var command = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.SetParam,
                PerformerEntity = payload.SingleRootPerformer,
                ParamKey = paramKey,
                ParamLane = ParamLane.Int,
                IntValue = selected ? 1 : 0,
            };
            if (!commands.TryAdd(command))
            {
                throw new InvalidOperationException("MassNavWebParityMod selection performer commands exceeded PerformerCommandBuffer capacity.");
            }
        }

        _lastSelectionRevision = _simulation.SelectionRevision;
        _lastStructuralRevision = _simulation.StructuralChangeRevision;
    }

    private void EnsureSelectionCache(int required)
    {
        if (_selectionCache.Length < required)
        {
            Array.Resize(ref _selectionCache, required);
        }
    }
}
