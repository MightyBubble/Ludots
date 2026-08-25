using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Platform.Abstractions;
using PersistenceOnlineReplayShowcaseMod.Runtime;

namespace PersistenceOnlineReplayShowcaseMod.Systems;

internal sealed class PersistenceOnlineReplayPresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly PersistenceOnlineReplayRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private static readonly QueryDescription NamedPositionQuery = new QueryDescription().WithAll<Name, WorldPositionCm>();

    public PersistenceOnlineReplayPresentationSystem(GameEngine engine, PersistenceOnlineReplayRuntime runtime, DebugDrawCommandBuffer debugDraw) : base(engine.World)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _debugDraw = debugDraw ?? throw new ArgumentNullException(nameof(debugDraw));
    }

    public override void Update(in float dt)
    {
        _runtime.RefreshPanel(_engine);
        if (!PersistenceOnlineReplayShowcaseIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value)) return;
        _debugDraw.Clear();
        _engine.World.Query(in NamedPositionQuery, (Entity _, ref Name name, ref WorldPositionCm position) =>
        {
            if (string.IsNullOrWhiteSpace(name.Value) || name.Value.IndexOf("Replay", StringComparison.OrdinalIgnoreCase) < 0) return;
            Vector2 current = new(position.Value.X.ToFloat() * 0.01f, position.Value.Y.ToFloat() * 0.01f);
            DebugDrawColor color = _runtime.IsDisconnected ? new DebugDrawColor(255, 88, 88) : _runtime.IsReplayPlaying ? new DebugDrawColor(255, 202, 72) : new DebugDrawColor(72, 226, 210);
            _debugDraw.Circles.Add(new DebugDrawCircle2D { Center = current, Radius = 4.2f, Thickness = 0.24f, Color = color });
            _debugDraw.Circles.Add(new DebugDrawCircle2D { Center = current, Radius = 2.2f, Thickness = 0.18f, Color = color });
            _debugDraw.Boxes.Add(new DebugDrawBox2D { Center = current, HalfWidth = 2.0f, HalfHeight = 2.0f, RotationRadians = 0f, Thickness = 0.18f, Color = color });
            foreach (ReplayVisualMarker marker in _runtime.CheckpointVisuals)
            {
                if (!string.Equals(marker.Name, name.Value, StringComparison.OrdinalIgnoreCase)) continue;
                Vector2 checkpoint = new(marker.XCm * 0.01f, marker.YCm * 0.01f);
                _debugDraw.Boxes.Add(new DebugDrawBox2D { Center = checkpoint, HalfWidth = 4.0f, HalfHeight = 4.0f, RotationRadians = 0f, Thickness = 0.24f, Color = new DebugDrawColor(238, 94, 220, 220) });
                _debugDraw.Circles.Add(new DebugDrawCircle2D { Center = checkpoint, Radius = 4.8f, Thickness = 0.22f, Color = new DebugDrawColor(238, 94, 220, 200) });
                if (Vector2.DistanceSquared(current, checkpoint) > 0.0025f)
                    _debugDraw.Lines.Add(new DebugDrawLine2D { A = checkpoint, B = current, Thickness = 0.06f, Color = new DebugDrawColor(238, 94, 220, 180) });
                break;
            }
        });
    }
}
