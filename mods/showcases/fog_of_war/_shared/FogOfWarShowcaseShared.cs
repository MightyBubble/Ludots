using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Vision;

namespace WarFogShowcase.Shared;

public enum FogShowcaseKind
{
    MultiLayer,
    VisionConeHighGround,
    LineOfSightBrush,
    ExploredMemory,
    GapGenerator,
    StealthDetection,
    SharedVisionSnapshot,
    FogOfWar
}

public readonly struct FogShowcaseScenario
{
    private FogShowcaseScenario(
        FogShowcaseKind kind,
        string modName,
        int scopeKeyId,
        int overlayStableId,
        VisionAperture aperture,
        int altitudeBand,
        uint layerSelector)
    {
        Kind = kind;
        ModName = modName;
        ScopeKeyId = scopeKeyId;
        OverlayStableId = overlayStableId;
        Aperture = aperture;
        AltitudeBand = altitudeBand;
        LayerSelector = layerSelector;
    }

    public FogShowcaseKind Kind { get; }
    public string ModName { get; }
    public int ScopeKeyId { get; }
    public int OverlayStableId { get; }
    public VisionAperture Aperture { get; }
    public int AltitudeBand { get; }
    public uint LayerSelector { get; }
    public string InstalledKey => $"{ModName}.Installed";
    public string RuntimeServiceKey => $"{ModName}.Runtime";

    public static FogShowcaseScenario Create(FogShowcaseKind kind, string modName)
    {
        return kind switch
        {
            FogShowcaseKind.MultiLayer => new(kind, modName, 307, 7307, VisionAperture.Disk(3600), altitudeBand: 0, layerSelector: 0b011u),
            FogShowcaseKind.VisionConeHighGround => new(kind, modName, 308, 7308, VisionAperture.Cone(5200, 42), altitudeBand: 1, layerSelector: 0b001u),
            FogShowcaseKind.LineOfSightBrush => new(kind, modName, 309, 7309, VisionAperture.Cone(5600, 35), altitudeBand: 0, layerSelector: 0b001u),
            FogShowcaseKind.ExploredMemory => new(kind, modName, 310, 7310, VisionAperture.Disk(3000), altitudeBand: 0, layerSelector: 0b001u),
            FogShowcaseKind.GapGenerator => new(kind, modName, 311, 7311, VisionAperture.Line(6200, 650), altitudeBand: 0, layerSelector: 0b001u),
            FogShowcaseKind.StealthDetection => new(kind, modName, 312, 7312, VisionAperture.Disk(4200), altitudeBand: 0, layerSelector: 0b101u),
            FogShowcaseKind.SharedVisionSnapshot => new(kind, modName, 313, 7313, VisionAperture.Disk(3400), altitudeBand: 0, layerSelector: 0b001u),
            FogShowcaseKind.FogOfWar => new(kind, modName, 315, 7315, VisionAperture.Cone(6200, 55), altitudeBand: 0, layerSelector: 0b001u),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported fog showcase kind.")
        };
    }
}

public sealed class FogShowcaseRuntime
{
    private readonly FogShowcaseScenario _scenario;
    private Entity _viewer = Entity.Null;
    private readonly Entity[] _occupants = new Entity[4];
    private FogLayerId _groundLayer;
    private FogLayerId _airLayer;
    private FogLayerId _detectionLayer;
    private uint _groundMask;
    private uint _airMask;
    private uint _detectionMask;
    private int _tick;

    public FogShowcaseRuntime(FogShowcaseScenario scenario)
    {
        _scenario = scenario;
    }

    public FogShowcaseScenario Scenario => _scenario;

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine != null)
        {
            Configure(engine, resetTick: true);
        }

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine != null)
        {
            DestroyOwnedEntities(engine.World);
            if (engine.TryGetService(CoreServiceKeys.VisionFogFieldStore, out FogFieldStore fields))
            {
                ClearScenarioFields(fields);
            }
        }

        return Task.CompletedTask;
    }

    public void Tick(GameEngine engine, float dt)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        Configure(engine, resetTick: false);
        _tick++;
        int centerX = (int)MathF.Round(MathF.Cos(_tick * 0.025f) * 1800f);
        int centerY = (int)MathF.Round(MathF.Sin(_tick * 0.018f) * 1400f);
        int tangentX = -centerY;
        int tangentY = centerX;
        if (tangentX == 0 && tangentY == 0)
        {
            tangentX = 1;
        }

        int facingDeg = WorldPlane2D.FacingDegreesPositiveFromDirection(tangentX, tangentY);
        float facingRad = WorldPlane2D.DegToRadValue(facingDeg);

        ref WorldPositionCm position = ref engine.World.Get<WorldPositionCm>(_viewer);
        position = WorldPositionCm.FromCm(centerX, centerY);
        ref FacingDirection facing = ref engine.World.Get<FacingDirection>(_viewer);
        facing.AngleRad = facingRad;
        ref VisionEmitterCm emitter = ref engine.World.Get<VisionEmitterCm>(_viewer);
        emitter.Aperture = _scenario.Aperture;
        emitter.AltitudeBand = _scenario.AltitudeBand;
        emitter.LayerMask = ResolveLayerMask();
        emitter.DetectionStrength = _scenario.Kind == FogShowcaseKind.StealthDetection ? (byte)2 : (byte)1;
        emitter.TrueSightStrength = _scenario.Kind == FogShowcaseKind.StealthDetection ? (byte)1 : (byte)0;
    }

    public void EmitPresentation(GameEngine engine)
    {
        if (engine == null || !engine.World.IsAlive(_viewer))
        {
            return;
        }

        if (engine.TryGetService(CoreServiceKeys.RenderDebugState, out RenderDebugState debugState) && debugState != null)
        {
            debugState.DrawFieldOverlays = true;
        }

        if (!engine.TryGetService(CoreServiceKeys.GroundOverlayBuffer, out GroundOverlayBuffer overlays) || overlays == null)
        {
            return;
        }

        ref WorldPositionCm position = ref engine.World.Get<WorldPositionCm>(_viewer);
        ref FacingDirection facing = ref engine.World.Get<FacingDirection>(_viewer);
        ref VisionEmitterCm emitter = ref engine.World.Get<VisionEmitterCm>(_viewer);
        Vector3 center = WorldPlane2D.LogicCmToVisualMeters(position.Value.X.ToFloat(), position.Value.Y.ToFloat(), 0.08f);
        GroundOverlayShape shape = emitter.Aperture.Kind == VisionApertureKind.Cone
            ? GroundOverlayShape.Cone
            : emitter.Aperture.Kind == VisionApertureKind.Line
                ? GroundOverlayShape.Line
                : GroundOverlayShape.Circle;
        overlays.Upsert(new GroundOverlayItem
        {
            StableId = _scenario.OverlayStableId,
            Shape = shape,
            Center = center,
            Radius = emitter.Aperture.RangeCm / 100f,
            Angle = WorldPlane2D.DegToRadValue(emitter.Aperture.HalfAngleDeg),
            Rotation = facing.AngleRad,
            Length = emitter.Aperture.RangeCm / 100f,
            Width = Math.Max(1f, emitter.Aperture.HalfWidthCm / 50f),
            FillColor = new Vector4(0.15f, 0.65f, 1f, 0.16f),
            BorderColor = new Vector4(0.25f, 0.85f, 1f, 0.78f),
            BorderWidth = 0.045f
        });
    }

    private void Configure(GameEngine engine, bool resetTick)
    {
        FogLayerRegistry layers = engine.GetService(CoreServiceKeys.VisionFogLayerRegistry)
            ?? throw new InvalidOperationException($"{_scenario.ModName} requires VisionFogLayerRegistry.");
        FogFieldStore fields = engine.GetService(CoreServiceKeys.VisionFogFieldStore)
            ?? throw new InvalidOperationException($"{_scenario.ModName} requires VisionFogFieldStore.");
        FogCellMap cellMap = engine.GetService(CoreServiceKeys.VisionFogCellMap)
            ?? throw new InvalidOperationException($"{_scenario.ModName} requires VisionFogCellMap.");

        _groundLayer = EnsureLayer(layers, "war-fog-ground", cellSizeCm: 500, updateHz: 10);
        _airLayer = EnsureLayer(layers, "war-fog-air", cellSizeCm: 750, updateHz: 5);
        _detectionLayer = EnsureLayer(layers, "war-fog-detection", cellSizeCm: 500, updateHz: 10);
        _groundMask = layers.ToMask(_groundLayer);
        _airMask = layers.ToMask(_airLayer);
        _detectionMask = layers.ToMask(_detectionLayer);

        ConfigureCellMap(cellMap);
        EnsureEntities(engine.World);
        SeedFields(fields, layers);
        if (resetTick)
        {
            _tick = 0;
        }
    }

    private uint ResolveLayerMask()
    {
        uint mask = 0u;
        if ((_scenario.LayerSelector & 0b001u) != 0u)
        {
            mask |= _groundMask;
        }

        if ((_scenario.LayerSelector & 0b010u) != 0u)
        {
            mask |= _airMask;
        }

        if ((_scenario.LayerSelector & 0b100u) != 0u)
        {
            mask |= _detectionMask;
        }

        return mask == 0u ? _groundMask : mask;
    }

    private void EnsureEntities(World world)
    {
        if (!world.IsAlive(_viewer))
        {
            _viewer = world.Create(
                WorldPositionCm.FromCm(-1600, -900),
                new FacingDirection { AngleRad = WorldPlane2D.DegToRadValue(20) },
                new VisionEmitterCm
                {
                    ScopeKeyId = _scenario.ScopeKeyId,
                    LayerMask = ResolveLayerMask(),
                    Polarity = VisionPolarity.Reveal,
                    Aperture = _scenario.Aperture,
                    AltitudeBand = _scenario.AltitudeBand,
                    DetectionStrength = 1
                });
        }

        EnsureOccupant(world, 0, -650, 350, _groundMask, stealthLevel: 0);
        EnsureOccupant(world, 1, 1850, -450, _groundMask | _detectionMask, stealthLevel: _scenario.Kind == FogShowcaseKind.StealthDetection ? (byte)2 : (byte)0);
        EnsureOccupant(world, 2, -2400, 1550, _groundMask | _airMask, stealthLevel: 0);
        EnsureOccupant(world, 3, 2750, 1250, _airMask | _detectionMask, stealthLevel: 1);
    }

    private void EnsureOccupant(World world, int index, int x, int y, uint layerMask, byte stealthLevel)
    {
        if (world.IsAlive(_occupants[index]))
        {
            ref WorldPositionCm position = ref world.Get<WorldPositionCm>(_occupants[index]);
            position = WorldPositionCm.FromCm(x, y);
            ref FogOccupantCm occupant = ref world.Get<FogOccupantCm>(_occupants[index]);
            occupant.ExposeLayerMask = layerMask;
            occupant.StealthLevel = stealthLevel;
            return;
        }

        _occupants[index] = world.Create(
            WorldPositionCm.FromCm(x, y),
            new FogOccupantCm
            {
                ExposeLayerMask = layerMask,
                StealthLevel = stealthLevel
            });
    }

    private void SeedFields(FogFieldStore fields, FogLayerRegistry layers)
    {
        FogField ground = fields.GetOrCreate(_scenario.ScopeKeyId, layers.Get(_groundLayer));
        for (int y = -9; y <= 9; y++)
        {
            for (int x = -9; x <= 9; x++)
            {
                int d2 = (x * x) + (y * y);
                if (d2 > 30 && d2 <= 90)
                {
                    ground.SetExplored(new FogCell(x, y));
                }
            }
        }

        for (int y = -2; y <= 2; y++)
        {
            for (int x = 4; x <= 9; x++)
            {
                ground.SetDenied(new FogCell(x, y));
            }
        }

        if (_scenario.Kind is FogShowcaseKind.FogOfWar or FogShowcaseKind.GapGenerator)
        {
            for (int y = -4; y <= 4; y++)
            {
                for (int x = -9; x <= -5; x++)
                {
                    ground.SetVisible(new FogCell(x, y));
                }
            }
        }

        ground.MarkDirtyRegion(new IntRect(-10, -10, 20, 20));

        if ((_scenario.LayerSelector & 0b010u) != 0u)
        {
            FogField air = fields.GetOrCreate(_scenario.ScopeKeyId, layers.Get(_airLayer));
            air.SetExplored(new FogCell(0, 0));
            air.SetExplored(new FogCell(1, 0));
            air.SetExplored(new FogCell(0, 1));
            air.MarkDirtyRegion(new IntRect(-1, -1, 4, 4));
        }

        if ((_scenario.LayerSelector & 0b100u) != 0u)
        {
            FogField detection = fields.GetOrCreate(_scenario.ScopeKeyId, layers.Get(_detectionLayer));
            detection.SetExplored(new FogCell(3, -1));
            detection.MarkDirtyRegion(new IntRect(2, -2, 4, 4));
        }
    }

    private static void ConfigureCellMap(FogCellMap cellMap)
    {
        for (int y = -4; y <= 4; y++)
        {
            cellMap.SetOpaque(new FogCell(2, y), true);
        }

        for (int x = -3; x <= 3; x++)
        {
            cellMap.SetHeightTier(new FogCell(x, 3), 2);
        }

        cellMap.SetConcealed(new FogCell(4, -1), true);
        cellMap.SetConcealed(new FogCell(5, -1), true);
    }

    private void ClearScenarioFields(FogFieldStore fields)
    {
        FogField[] scratch = new FogField[Math.Max(1, fields.Count)];
        int count = fields.CopyFields(scratch);
        for (int i = 0; i < count; i++)
        {
            if (scratch[i].ScopeKeyId == _scenario.ScopeKeyId)
            {
                scratch[i].Clear();
            }
        }
    }

    private void DestroyOwnedEntities(World world)
    {
        if (world.IsAlive(_viewer))
        {
            world.Destroy(_viewer);
            _viewer = Entity.Null;
        }

        for (int i = 0; i < _occupants.Length; i++)
        {
            if (world.IsAlive(_occupants[i]))
            {
                world.Destroy(_occupants[i]);
                _occupants[i] = Entity.Null;
            }
        }
    }

    private static FogLayerId EnsureLayer(FogLayerRegistry registry, string key, int cellSizeCm, int updateHz)
    {
        FogLayerId existing = registry.GetId(key);
        return existing.Value > 0 ? existing : registry.Register(key, cellSizeCm, updateHz);
    }
}

public sealed class FogShowcaseSimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly FogShowcaseRuntime _runtime;

    public FogShowcaseSimulationSystem(GameEngine engine, FogShowcaseRuntime runtime)
        : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
    {
        _engine = engine;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override void Update(in float dt)
    {
        _runtime.Tick(_engine, dt);
    }
}

public sealed class FogShowcasePresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly FogShowcaseRuntime _runtime;

    public FogShowcasePresentationSystem(GameEngine engine, FogShowcaseRuntime runtime)
        : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
    {
        _engine = engine;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override void Update(in float dt)
    {
        _runtime.EmitPresentation(_engine);
    }
}
