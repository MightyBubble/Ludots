using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Platform.Abstractions;
using SaveLoadShowcaseMod.Runtime;
using SaveLoadShowcaseMod.UI;

namespace SaveLoadShowcaseMod.Systems;

internal sealed class SaveLoadShowcasePresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly SaveLoadShowcaseRuntime _runtime;
    private readonly SaveLoadShowcasePanelController _panel;
    private readonly DebugDrawCommandBuffer _debugDraw;

    public SaveLoadShowcasePresentationSystem(GameEngine engine, SaveLoadShowcaseRuntime runtime, DebugDrawCommandBuffer debugDraw) : base(engine.World)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _debugDraw = debugDraw ?? throw new ArgumentNullException(nameof(debugDraw));
        _panel = new SaveLoadShowcasePanelController(runtime);
    }

    public override void Update(in float dt)
    {
        if (!_runtime.IsShowcaseMap)
        {
            _panel.ClearIfOwned();
            return;
        }

        _panel.MountOrRefresh(_engine);
        DrawMarkers();
    }

    private void DrawMarkers()
    {
        _debugDraw.Clear();
        (int hx, int hy) heroCm = default;
        Entity hero = Entity.Null;
        var heroQuery = new QueryDescription().WithAll<Name, WorldPositionCm>();
        _engine.World.Query(in heroQuery, (Entity e, ref Name name, ref WorldPositionCm pos) =>
        {
            if (name.Value == SaveLoadShowcaseIds.HeroName)
            {
                hero = e;
                var cm = pos.ToWorldCmInt2();
                heroCm = (cm.X, cm.Y);
            }
        });

        if (hero == Entity.Null) return;
        Vector2 heroPos = new(heroCm.hx * 0.01f, heroCm.hy * 0.01f);
        var cyan = new DebugDrawColor(72, 226, 210);
        _debugDraw.Circles.Add(new DebugDrawCircle2D { Center = heroPos, Radius = 3.2f, Thickness = 0.22f, Color = cyan });

        if (_runtime.SavedHeroCm is { } saved)
        {
            Vector2 savedPos = new(saved.x * 0.01f, saved.y * 0.01f);
            var magenta = new DebugDrawColor(238, 94, 220);
            _debugDraw.Boxes.Add(new DebugDrawBox2D { Center = savedPos, HalfWidth = 2.6f, HalfHeight = 2.6f, RotationRadians = 0f, Thickness = 0.22f, Color = magenta });
            _debugDraw.Circles.Add(new DebugDrawCircle2D { Center = savedPos, Radius = 4.6f, Thickness = 0.18f, Color = magenta });
            _debugDraw.Lines.Add(new DebugDrawLine2D { A = savedPos, B = heroPos, Thickness = 0.08f, Color = magenta });
        }
    }
}
