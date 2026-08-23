using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.EntityHistory;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;

namespace EffectHistoryShowcaseMod.Runtime;

public sealed class EffectHistoryShowcaseRuntime
{
    private World? _world;
    private Entity _viewer;
    private Entity _target;
    private int _tick;
    private bool _started;

    public EntitySnapshotStore EntitySnapshots { get; } = new(16);
    public KnowledgeSnapshotStore KnowledgeSnapshots { get; } = new(16);
    public EffectExecutionRecordStore ExecutionRecords { get; } = new(32);
    public EffectTargetResolveResult LastResult { get; private set; }
    public int Tick => _tick;

    public Task HandleMapLoadedAsync(ScriptContext context)
    {
        _world = context.GetWorld();
        _started = false;
        _tick = 0;
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        _world = null;
        _started = false;
        return Task.CompletedTask;
    }

    public void Advance(GameEngine engine)
    {
        if (_world == null || engine.CurrentMapSession?.MapId.Value != EffectHistoryShowcaseIds.MapId)
            return;

        _tick++;
        if (!_started)
        {
            _viewer = _world.Create();
            _target = _world.Create();
            _started = true;
            RunLiveAndKnown();
        }
        else if (_tick == 4 && _world.IsAlive(_target))
        {
            _world.Destroy(_target);
            RunStale();
            Entity reused = _world.Create();
            LastResult = reused == _target ? EffectTargetResolveResult.Stale : LastResult;
        }
    }

    private void RunLiveAndKnown()
    {
        EntityRef viewer = EntityRef.From(_viewer);
        EntityRef target = EntityRef.From(_target);
        var snapshot = new KnowledgeSnapshot
        {
            Viewer = viewer,
            Target = target,
            Presence = KnowledgePresence.Known,
            PositionAccess = KnowledgePositionAccess.LastKnown,
            Position = Fix64Vec2.FromInt(8, 4),
            HasPosition = 1,
            ObservedTick = _tick,
            ExpiryTick = _tick + 3,
            Revision = 1,
        };
        KnowledgeSnapshots.Upsert(in snapshot);
        var reference = new EffectTargetRef(in target, in viewer, EffectTargetResolutionMode.LastKnown, _tick, 1, _tick + 3, snapshot.Position, 0);
        EffectTargetResolveOutput output = EffectTargetResolver.Resolve(_world!, in reference, _tick, EntitySnapshots, KnowledgeSnapshots);
        LastResult = output.Result;
        var context = new EffectContext { RootId = 1087, Source = _viewer, Target = _target };
        KnowledgeIdMask256 empty = default;
        var record = EffectExecutionRecordFactory.Create(in context, 1, in reference, _tick, output.Result, 2, 3, 0, in empty, in empty);
        ExecutionRecords.TryAdd(in record, out _);
    }

    private void RunStale()
    {
        EntityRef viewer = EntityRef.From(_viewer);
        EntityRef target = EntityRef.From(_target);
        var reference = new EffectTargetRef(in target, in viewer, EffectTargetResolutionMode.LastKnown, _tick, 1, _tick, Fix64Vec2.Zero, 0);
        EffectTargetResolveOutput output = EffectTargetResolver.Resolve(_world!, in reference, _tick, EntitySnapshots, KnowledgeSnapshots);
        LastResult = output.Result;
    }
}
