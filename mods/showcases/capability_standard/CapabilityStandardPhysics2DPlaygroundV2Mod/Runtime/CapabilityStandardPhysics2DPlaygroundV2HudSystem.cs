using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;

internal sealed class CapabilityStandardPhysics2DPlaygroundV2HudSystem : ISystem<float>
{
    private static readonly QueryDescription _physicsStatsQuery = new QueryDescription()
        .WithAll<Physics2DPerfStats>();
    private static readonly QueryDescription _staticPolygonQuery = new QueryDescription()
        .WithAll<EntityTemplateKeyRef, Position2D, Collider2D>();

    private static readonly Vector4 PanelFill = new(0.03f, 0.045f, 0.055f, 0.78f);
    private static readonly Vector4 PanelBorder = new(0.42f, 0.62f, 0.72f, 0.92f);
    private static readonly Vector4 TitleColor = new(0.92f, 0.98f, 1.0f, 1f);
    private static readonly Vector4 StatsColor = new(0.74f, 0.88f, 1.0f, 1f);
    private static readonly Vector4 TextColor = new(0.86f, 0.92f, 0.95f, 1f);
    private static readonly Vector4 HintColor = new(0.94f, 0.78f, 0.45f, 1f);
    private static readonly Vector4 PolygonStroke = new(0.40f, 0.82f, 1.0f, 0.96f);

    private readonly GameEngine _engine;
    private readonly CapabilityStandardPhysics2DPlaygroundV2Config _config;
    private readonly ShapeDataStorage2D _shapeStorage;
    private int _staticPolygonTemplateKeyId;

    public CapabilityStandardPhysics2DPlaygroundV2HudSystem(
        GameEngine engine,
        CapabilityStandardPhysics2DPlaygroundV2Config config)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _shapeStorage = engine.GetService(CoreServiceKeys.Physics2DShapeStorage) as ShapeDataStorage2D
            ?? throw new InvalidOperationException("Physics2D Playground v2 HUD requires Physics2D shape storage.");
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!CapabilityStandardPhysics2DPlaygroundV2State.Enabled ||
            _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
        {
            return;
        }

        RenderHud(overlay, dt);
        DrawStaticPolygonOutlines(overlay);
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    private void RenderHud(ScreenOverlayBuffer overlay, float dt)
    {
        float frameMs = dt * 1000f;
        int fps = dt > 0.0001f ? (int)MathF.Round(1f / dt) : 0;
        Physics2DPerfStats stats = TryReadPhysicsStats(out Physics2DPerfStats value) ? value : default;

        int total = ReadInt(CapabilityStandardPhysics2DPlaygroundV2State.TotalEntityCountServiceKey);
        int physicsOnly = ReadInt(CapabilityStandardPhysics2DPlaygroundV2State.PhysicsOnlyEntityCountServiceKey);
        int nav = ReadInt(CapabilityStandardPhysics2DPlaygroundV2State.NavEntityCountServiceKey);
        int benchmark = ReadInt(CapabilityStandardPhysics2DPlaygroundV2State.BenchmarkEntityCountServiceKey);
        int benchmarkSpawnCount = ReadInt(CapabilityStandardPhysics2DPlaygroundV2State.BenchmarkSpawnCountServiceKey);
        int staticPolygons = ReadInt(CapabilityStandardPhysics2DPlaygroundV2State.StaticPolygonCountServiceKey);
        int frictionZones = ReadInt(CapabilityStandardPhysics2DPlaygroundV2State.FrictionZoneCountServiceKey);
        int explosionAffected = ReadInt(CapabilityStandardPhysics2DPlaygroundV2State.ExplosionLastAffectedServiceKey);
        string mode = ReadString(CapabilityStandardPhysics2DPlaygroundV2State.ActiveModeServiceKey, CapabilityStandardPhysics2DPlaygroundV2State.ActiveMode.ToString());
        string lastAction = ReadString(CapabilityStandardPhysics2DPlaygroundV2State.LastActionServiceKey, "ready");

        const int x = 14;
        const int y = 14;
        const int w = 850;
        const int h = 178;

        overlay.AddRect(x, y, w, h, PanelFill, PanelBorder, stableId: 62000, dirtySerial: 1);
        overlay.AddText(x + 14, y + 10, "Physics2D Playground v2", 20, TitleColor, stableId: 62001, dirtySerial: 1);
        overlay.AddText(
            x + 14,
            y + 38,
            $"FPS {fps} | Frame {frameMs:0.00}ms | Physics {stats.PhysicsUpdateMs:0.0000}ms | PhysicsHz {stats.PhysicsHz} | Steps {stats.PhysicsStepsLastFixedTick}",
            14,
            StatsColor,
            stableId: 62002,
            dirtySerial: HashCode.Combine(fps, frameMs, stats.PhysicsUpdateMs, stats.PhysicsHz, stats.PhysicsStepsLastFixedTick));
        overlay.AddText(
            x + 14,
            y + 60,
            $"Entities total {total} | physics {physicsOnly} | nav {nav} | benchmark {benchmark}/{benchmarkSpawnCount} | polygon {staticPolygons} | friction {frictionZones} | explosion last {explosionAffected}",
            14,
            TextColor,
            stableId: 62003,
            dirtySerial: HashCode.Combine(total, physicsOnly, nav, benchmark, benchmarkSpawnCount, staticPolygons, frictionZones, explosionAffected));
        overlay.AddText(
            x + 14,
            y + 82,
            $"Pairs potential {stats.PotentialPairs} | contacts {stats.ContactPairs} | mode {mode} | {lastAction}",
            14,
            TextColor,
            stableId: 62004,
            dirtySerial: HashCode.Combine(stats.PotentialPairs, stats.ContactPairs, mode, lastAction));
        overlay.AddText(
            x + 14,
            y + 110,
            "1 Physics | 2 Nav | F1/F2/F3 camera | RMB benchmark burst | LeftShift+Q/W/E/R/T/Y/U/O/P count 10..90",
            13,
            HintColor,
            stableId: 62005,
            dirtySerial: 1);
        overlay.AddText(
            x + 14,
            y + 132,
            "I impulse | K knockback/CC | C GAS force | G static polygon | F friction zones | X explosion | N nav move",
            13,
            HintColor,
            stableId: 62006,
            dirtySerial: 1);
        overlay.AddText(
            x + 14,
            y + 154,
            $"Explosion radius {_config.ExplosionRadiusCm}cm force {_config.ExplosionForceCmPerSec2}cm/s2 | friction zones low/medium/high",
            13,
            StatsColor,
            stableId: 62007,
            dirtySerial: HashCode.Combine(_config.ExplosionRadiusCm, _config.ExplosionForceCmPerSec2));
    }

    private void DrawStaticPolygonOutlines(ScreenOverlayBuffer overlay)
    {
        if (_engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector)
        {
            return;
        }

        int templateKeyId = ResolveStaticPolygonTemplateKeyId();
        int stableId = 62100;
        _engine.World.Query(
            in _staticPolygonQuery,
            (Entity entity, ref EntityTemplateKeyRef keyRef, ref Position2D position, ref Collider2D collider) =>
            {
                if (keyRef.TemplateKeyId != templateKeyId ||
                    collider.Type != ColliderType2D.Polygon ||
                    !_shapeStorage.TryGetPolygon(collider.ShapeDataIndex, out PolygonShapeData polygon) ||
                    polygon.Vertices == null ||
                    polygon.VertexCount < 3)
                {
                    return;
                }

                Fix64 rotation = _engine.World.Has<Rotation2D>(entity)
                    ? _engine.World.Get<Rotation2D>(entity).Value
                    : Fix64.Zero;
                for (int i = 0; i < polygon.VertexCount; i++)
                {
                    Fix64Vec2 a = ResolvePolygonWorldVertex(in position, rotation, in polygon, i);
                    Fix64Vec2 b = ResolvePolygonWorldVertex(in position, rotation, in polygon, (i + 1) % polygon.VertexCount);
                    if (!TryProject(projector, in a, out Vector2 screenA) ||
                        !TryProject(projector, in b, out Vector2 screenB))
                    {
                        continue;
                    }

                    overlay.AddLine(
                        (int)MathF.Round(screenA.X),
                        (int)MathF.Round(screenA.Y),
                        (int)MathF.Round(screenB.X),
                        (int)MathF.Round(screenB.Y),
                        thickness: 2,
                        PolygonStroke,
                        stableId,
                        dirtySerial: 1);
                    stableId++;
                }
            });
    }

    private bool TryReadPhysicsStats(out Physics2DPerfStats stats)
    {
        bool found = false;
        Physics2DPerfStats captured = default;
        _engine.World.Query(in _physicsStatsQuery, (ref Physics2DPerfStats value) =>
        {
            if (!found)
            {
                captured = value;
                found = true;
            }
        });
        stats = captured;
        return found;
    }

    private int ResolveStaticPolygonTemplateKeyId()
    {
        if (_staticPolygonTemplateKeyId > 0)
        {
            return _staticPolygonTemplateKeyId;
        }

        EntityTemplateKeyRegistry templateKeys = _engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
        if (!templateKeys.TryGetId(_config.StaticPolygonTemplateId, out _staticPolygonTemplateKeyId) ||
            _staticPolygonTemplateKeyId <= 0)
        {
            throw new InvalidOperationException($"Physics2D Playground v2 static polygon template '{_config.StaticPolygonTemplateId}' is not registered.");
        }

        return _staticPolygonTemplateKeyId;
    }

    private int ReadInt(string key)
    {
        return _engine.GlobalContext.TryGetValue(key, out object? value) && value is int number
            ? number
            : 0;
    }

    private string ReadString(string key, string fallback)
    {
        return _engine.GlobalContext.TryGetValue(key, out object? value) && value is string text
            ? text
            : fallback;
    }

    private static Fix64Vec2 ResolvePolygonWorldVertex(
        in Position2D position,
        Fix64 rotation,
        in PolygonShapeData polygon,
        int vertexIndex)
    {
        Fix64Vec2 local = polygon.LocalOffset + polygon.Vertices[vertexIndex] - polygon.LocalCenter;
        if (rotation == Fix64.Zero)
        {
            return position.Value + local;
        }

        Fix64 sin = Fix64Math.Sin(rotation);
        Fix64 cos = Fix64Math.Cos(rotation);
        Fix64Vec2 rotated = new(
            (cos * local.X) - (sin * local.Y),
            (sin * local.X) + (cos * local.Y));
        return position.Value + rotated;
    }

    private static bool TryProject(IScreenProjector projector, in Fix64Vec2 worldCm, out Vector2 screen)
    {
        screen = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(in worldCm, yMeters: 0.12f));
        return float.IsFinite(screen.X) && float.IsFinite(screen.Y);
    }
}
