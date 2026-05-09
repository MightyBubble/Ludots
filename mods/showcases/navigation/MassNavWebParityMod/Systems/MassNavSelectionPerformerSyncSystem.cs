using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavSelectionPerformerSyncSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;
    private bool[] _selectionCache = Array.Empty<bool>();
    private bool[] _markerCache = Array.Empty<bool>();
    private uint _lastSelectionRevision = uint.MaxValue;
    private int _lastStructuralRevision = -1;
    private bool _pendingMarkerRetry;
    private int _lightMarkerDefinitionId;
    private int _heavyMarkerDefinitionId;

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
            _lastStructuralRevision == _simulation.StructuralChangeRevision &&
            !_pendingMarkerRetry)
        {
            return;
        }

        _pendingMarkerRetry = false;

        PerformerCommandBuffer commands = _engine.GetService(CoreServiceKeys.PerformerCommandBuffer)
            ?? throw new InvalidOperationException("MassNavWebParityMod requires PerformerCommandBuffer for selection presentation.");
        PerformerDefinitionRegistry definitions = _engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
            ?? throw new InvalidOperationException("MassNavWebParityMod requires PerformerDefinitionRegistry for selection marker presentation.");
        PerformerEntityRuntime runtime = _engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
            ?? throw new InvalidOperationException("MassNavWebParityMod requires PerformerEntityRuntime for selection marker ownership checks.");
        ResolveMarkerDefinitions(definitions);

        EnsureCaches(_simulation.AgentState.ControllableCount);

        for (int i = 0; i < _simulation.AgentState.ControllableCount; i++)
        {
            Entity owner = _simulation.AgentState.ControllableAgents[i];
            bool selected = i < _simulation.WebParity.SelectedFlags.Length && _simulation.WebParity.SelectedFlags[i] != 0;
            bool force = _lastStructuralRevision != _simulation.StructuralChangeRevision;
            bool wasSelected = _selectionCache[i];
            if (!force && wasSelected == selected)
            {
                continue;
            }

            if (!_engine.World.IsAlive(owner))
            {
                _selectionCache[i] = false;
                _markerCache[i] = false;
                continue;
            }

            if (selected)
            {
                _selectionCache[i] = SyncMarker(commands, runtime, owner, i);
                continue;
            }

            if (wasSelected || _markerCache[i])
            {
                DestroyMarkerIfPresent(commands, runtime, owner, i);
            }

            _selectionCache[i] = false;
        }

        _lastSelectionRevision = _simulation.SelectionRevision;
        _lastStructuralRevision = _simulation.StructuralChangeRevision;
    }

    private void EnsureCaches(int required)
    {
        if (_selectionCache.Length < required)
        {
            Array.Resize(ref _selectionCache, required);
        }

        if (_markerCache.Length < required)
        {
            Array.Resize(ref _markerCache, required);
        }
    }

    private void ResolveMarkerDefinitions(PerformerDefinitionRegistry definitions)
    {
        if (_lightMarkerDefinitionId <= 0)
        {
            _lightMarkerDefinitionId = ResolveDefinitionId(
                definitions,
                _simulation.Config.Presentation.SelectionMarkerLightPerformerId);
        }

        if (_heavyMarkerDefinitionId <= 0)
        {
            _heavyMarkerDefinitionId = ResolveDefinitionId(
                definitions,
                _simulation.Config.Presentation.SelectionMarkerHeavyPerformerId);
        }
    }

    private static int ResolveDefinitionId(PerformerDefinitionRegistry definitions, string definitionKey)
    {
        int definitionId = definitions.GetId(definitionKey);
        if (definitionId <= 0 || !definitions.TryGet(definitionId, out _))
        {
            throw new InvalidOperationException($"MassNavWebParityMod requires performer definition '{definitionKey}' for selection markers.");
        }

        return definitionId;
    }

    private bool SyncMarker(PerformerCommandBuffer commands, PerformerEntityRuntime runtime, Entity owner, int index)
    {
        if (!TryGetRootPerformer(owner, out Entity rootPerformer))
        {
            _selectionCache[index] = false;
            _pendingMarkerRetry = true;
            return false;
        }

        bool heavy = _engine.World.Has<MassNavAgentProfile>(owner) &&
                     _engine.World.Get<MassNavAgentProfile>(owner).Heavy;
        int definitionId = heavy ? _heavyMarkerDefinitionId : _lightMarkerDefinitionId;
        int staleDefinitionId = heavy ? _lightMarkerDefinitionId : _heavyMarkerDefinitionId;
        int scope = ResolveSelectionMarkerScope(owner);
        DestroyMarkerDefinitionIfPresent(commands, runtime, owner, staleDefinitionId, scope);

        if (runtime.TryGetActiveScopedInstance(
                definitionId,
                owner,
                scope,
                PresentationAnchorKind.Entity,
                default,
                out Entity marker))
        {
            if (IsAttachedToRoot(marker, rootPerformer))
            {
                _markerCache[index] = true;
                return true;
            }

            EnqueueDestroyMarker(commands, marker);
        }

        var command = new PerformerCommand
        {
            CommandKind = PerformerCommandKind.CreatePerformer,
            PerformerDefinitionId = definitionId,
            ParentEntity = rootPerformer,
            ScopeTag = scope,
            AnchorKind = PresentationAnchorKind.Entity,
            Source = owner,
        };
        if (!commands.TryAdd(command))
        {
            throw new InvalidOperationException("MassNavWebParityMod selection marker create commands exceeded PerformerCommandBuffer capacity.");
        }

        _markerCache[index] = true;
        return true;
    }

    private bool IsAttachedToRoot(Entity marker, Entity rootPerformer)
    {
        return marker != Entity.Null &&
               _engine.World.IsAlive(marker) &&
               _engine.World.Has<PerformerParent>(marker) &&
               _engine.World.Get<PerformerParent>(marker).Parent == rootPerformer;
    }

    private bool TryGetRootPerformer(Entity owner, out Entity rootPerformer)
    {
        rootPerformer = Entity.Null;
        if (!_engine.World.Has<PresentationOwnerHasPerformerPayload>(owner))
        {
            return false;
        }

        ref readonly PresentationOwnerHasPerformerPayload payload = ref _engine.World.Get<PresentationOwnerHasPerformerPayload>(owner);
        if (payload.SingleRootPerformer == Entity.Null ||
            !_engine.World.IsAlive(payload.SingleRootPerformer))
        {
            return false;
        }

        rootPerformer = payload.SingleRootPerformer;
        return true;
    }

    private void DestroyMarkerIfPresent(PerformerCommandBuffer commands, PerformerEntityRuntime runtime, Entity owner, int index)
    {
        int scope = ResolveSelectionMarkerScope(owner);
        DestroyMarkerDefinitionIfPresent(commands, runtime, owner, _lightMarkerDefinitionId, scope);
        DestroyMarkerDefinitionIfPresent(commands, runtime, owner, _heavyMarkerDefinitionId, scope);
        _markerCache[index] = false;
    }

    private bool DestroyMarkerDefinitionIfPresent(
        PerformerCommandBuffer commands,
        PerformerEntityRuntime runtime,
        Entity owner,
        int definitionId,
        int scope)
    {
        if (definitionId <= 0 ||
            !runtime.TryGetActiveScopedInstance(
                definitionId,
                owner,
                scope,
                PresentationAnchorKind.Entity,
                default,
                out Entity marker))
        {
            return false;
        }

        EnqueueDestroyMarker(commands, marker);
        return true;
    }

    private static void EnqueueDestroyMarker(PerformerCommandBuffer commands, Entity marker)
    {
        var command = new PerformerCommand
        {
            CommandKind = PerformerCommandKind.DestroyPerformer,
            PerformerEntity = marker,
        };
        if (!commands.TryAdd(command))
        {
            throw new InvalidOperationException("MassNavWebParityMod selection marker destroy commands exceeded PerformerCommandBuffer capacity.");
        }
    }

    private int ResolveSelectionMarkerScope(Entity owner)
    {
        if (!_engine.World.Has<PresentationStableId>(owner))
        {
            throw new InvalidOperationException("MassNavWebParityMod selection marker owner requires PresentationStableId.");
        }

        int stableId = _engine.World.Get<PresentationStableId>(owner).Value;
        if (stableId <= 0)
        {
            throw new InvalidOperationException($"MassNavWebParityMod selection marker owner has invalid PresentationStableId {stableId}.");
        }

        return stableId;
    }
}
