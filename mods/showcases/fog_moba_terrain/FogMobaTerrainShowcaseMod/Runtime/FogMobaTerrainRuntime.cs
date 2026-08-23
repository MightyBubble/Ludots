using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using FogMobaTerrainShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Vision;
using Ludots.Platform.Abstractions;
using Ludots.UI;

namespace FogMobaTerrainShowcaseMod.Runtime;

public sealed class FogMobaTerrainRuntime
{
    private const int ScopeKey = 318;
    private const int OverlayId = 7318;
    private const int CellSizeCm = 500;
    private readonly FogMobaTerrainPanelController _panel = new();
    private Entity _viewer = Entity.Null;
    private readonly Entity[] _contacts = new Entity[3];
    private FogLayerId _groundLayer;
    private uint _groundMask;
    private FogCellMap? _cellMap;
    private int _tick;
    private int _rangeCm = 5200;
    private bool _cone = true;
    private bool _rulesEnabled = true;
    private bool _memoryEnabled = true;
    private string _status = "Move with WASD. Turn with Left/Right. Watch the cyan field meet the walls and brush.";
    private int _wallCells;
    private int _brushCells;

    public FogMobaTerrainSnapshot Snapshot { get; private set; }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null || !IsMap(engine))
        {
            return Task.CompletedTask;
        }

        Configure(engine, reset: true);
        ActivateInput(engine.GetService(CoreServiceKeys.InputHandler));
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null) return Task.CompletedTask;
        DeactivateInput(engine.GetService(CoreServiceKeys.InputHandler));
        _panel.Clear(engine);
        Destroy(engine.World);
        return Task.CompletedTask;
    }

    public void Update(GameEngine engine)
    {
        if (!IsMap(engine)) return;
        Configure(engine, reset: false);
        if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            int dx = 0;
            int dy = 0;
            if (input.PressedThisFrame(FogMobaTerrainIds.MoveWest)) dx--;
            if (input.PressedThisFrame(FogMobaTerrainIds.MoveEast)) dx++;
            if (input.PressedThisFrame(FogMobaTerrainIds.MoveNorth)) dy++;
            if (input.PressedThisFrame(FogMobaTerrainIds.MoveSouth)) dy--;
            if (dx != 0 || dy != 0)
            {
                ref WorldPositionCm position = ref engine.World.Get<WorldPositionCm>(_viewer);
                WorldCmInt2 current = position.ToWorldCmInt2();
                position = WorldPositionCm.FromCm(
                    Math.Clamp(current.X + (dx * 120), -9000, 9000),
                    Math.Clamp(current.Y + (dy * 120), -6000, 6000));
                _status = "Observer moved; the Core field is recomputing.";
            }

            ref FacingDirection facing = ref engine.World.Get<FacingDirection>(_viewer);
            if (input.PressedThisFrame(FogMobaTerrainIds.TurnLeft)) facing.AngleRad -= 0.12f;
            if (input.PressedThisFrame(FogMobaTerrainIds.TurnRight)) facing.AngleRad += 0.12f;
            if (input.PressedThisFrame(FogMobaTerrainIds.ToggleShape))
            {
                _cone = !_cone;
                _status = _cone ? "Cone aperture enabled." : "Disk aperture enabled.";
            }
            if (input.PressedThisFrame(FogMobaTerrainIds.ToggleRules))
            {
                _rulesEnabled = !_rulesEnabled;
                ApplyTerrainRules();
                _status = _rulesEnabled ? "Walls and brush rules enabled." : "Rules disabled for the A/B comparison.";
            }
            if (input.PressedThisFrame(FogMobaTerrainIds.ToggleMemory))
            {
                _memoryEnabled = !_memoryEnabled;
                if (!_memoryEnabled) ClearExplored(engine);
                _status = _memoryEnabled ? "Explored memory enabled." : "Explored memory cleared.";
            }
            if (input.PressedThisFrame(FogMobaTerrainIds.ChangeRange))
            {
                _rangeCm = _rangeCm >= 7600 ? 3600 : _rangeCm + 1000;
                _status = $"Vision range {_rangeCm} cm.";
            }

            ref VisionEmitterCm emitter = ref engine.World.Get<VisionEmitterCm>(_viewer);
            emitter.Aperture = _cone ? VisionAperture.Cone(_rangeCm, 48) : VisionAperture.Disk(_rangeCm);
        }

        _tick++;
        RefreshSnapshot(engine);
    }

    public void Present(GameEngine engine)
    {
        if (!IsMap(engine) || !engine.World.IsAlive(_viewer)) return;
        if (engine.TryGetService(CoreServiceKeys.RenderDebugState, out RenderDebugState debug) && debug != null)
            debug.DrawFieldOverlays = true;
        if (engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is GroundOverlayBuffer overlays)
        {
            DrawTerrainOverlays(overlays);
            ref WorldPositionCm position = ref engine.World.Get<WorldPositionCm>(_viewer);
            ref FacingDirection facing = ref engine.World.Get<FacingDirection>(_viewer);
            Vector3 center = WorldPlane2D.LogicCmToVisualMeters(position.Value.X.ToFloat(), position.Value.Y.ToFloat(), 0.08f);
            ref VisionEmitterCm emitter = ref engine.World.Get<VisionEmitterCm>(_viewer);
            overlays.Upsert(new GroundOverlayItem
            {
                StableId = OverlayId,
                Shape = _cone ? GroundOverlayShape.Cone : GroundOverlayShape.Circle,
                Center = center,
                Radius = emitter.Aperture.RangeCm / 100f,
                Angle = WorldPlane2D.DegToRadValue(emitter.Aperture.HalfAngleDeg),
                Rotation = facing.AngleRad,
                Length = emitter.Aperture.RangeCm / 100f,
                Width = 1.5f,
                FillColor = new Vector4(0.05f, 0.75f, 0.95f, 0.14f),
                BorderColor = new Vector4(0.18f, 0.9f, 1f, 0.85f),
                BorderWidth = 0.05f
            });
        }
        _panel.Refresh(engine, Snapshot);
    }

    private static void DrawTerrainOverlays(GroundOverlayBuffer overlays)
    {
        Vector4 laneFill = new(0.62f, 0.68f, 0.72f, 0.28f);
        Vector4 laneBorder = new(0.78f, 0.86f, 0.9f, 0.65f);
        Vector4 riverFill = new(0.05f, 0.38f, 0.62f, 0.38f);
        Vector4 riverBorder = new(0.12f, 0.65f, 0.9f, 0.8f);
        Vector4 brushFill = new(0.08f, 0.55f, 0.22f, 0.34f);
        Vector4 brushBorder = new(0.2f, 0.9f, 0.35f, 0.9f);
        Vector4 wallFill = new(0.55f, 0.25f, 0.08f, 0.5f);
        Vector4 wallBorder = new(1f, 0.52f, 0.16f, 1f);
        overlays.Upsert(new GroundOverlayItem { StableId = 7401, Shape = GroundOverlayShape.Line, Center = new Vector3(-75f, 0.02f, -45f), Rotation = 0.52f, Length = 145f, Width = 7f, FillColor = laneFill, BorderColor = laneBorder, BorderWidth = 0.08f });
        overlays.Upsert(new GroundOverlayItem { StableId = 7402, Shape = GroundOverlayShape.Line, Center = new Vector3(-75f, 0.02f, 45f), Rotation = -0.52f, Length = 145f, Width = 7f, FillColor = laneFill, BorderColor = laneBorder, BorderWidth = 0.08f });
        overlays.Upsert(new GroundOverlayItem { StableId = 7403, Shape = GroundOverlayShape.Line, Center = new Vector3(-80f, 0.02f, 0f), Rotation = 0f, Length = 160f, Width = 8f, FillColor = laneFill, BorderColor = laneBorder, BorderWidth = 0.08f });
        overlays.Upsert(new GroundOverlayItem { StableId = 7404, Shape = GroundOverlayShape.Line, Center = new Vector3(-35f, 0.04f, 0f), Rotation = 0f, Length = 90f, Width = 15f, FillColor = riverFill, BorderColor = riverBorder, BorderWidth = 0.1f });
        overlays.Upsert(new GroundOverlayItem { StableId = 7410, Shape = GroundOverlayShape.Ring, Center = new Vector3(-20f, 0.05f, -20f), Radius = 10f, InnerRadius = 5f, FillColor = brushFill, BorderColor = brushBorder, BorderWidth = 0.1f });
        overlays.Upsert(new GroundOverlayItem { StableId = 7411, Shape = GroundOverlayShape.Ring, Center = new Vector3(-4f, 0.05f, 20f), Radius = 10f, InnerRadius = 5f, FillColor = brushFill, BorderColor = brushBorder, BorderWidth = 0.1f });
        overlays.Upsert(new GroundOverlayItem { StableId = 7412, Shape = GroundOverlayShape.Ring, Center = new Vector3(15f, 0.05f, -20f), Radius = 10f, InnerRadius = 5f, FillColor = brushFill, BorderColor = brushBorder, BorderWidth = 0.1f });
        overlays.Upsert(new GroundOverlayItem { StableId = 7420, Shape = GroundOverlayShape.Line, Center = new Vector3(0f, 0.06f, -25f), Rotation = MathF.PI * 0.5f, Length = 42f, Width = 2.5f, FillColor = wallFill, BorderColor = wallBorder, BorderWidth = 0.12f });
    }

    private void Configure(GameEngine engine, bool reset)
    {
        FogLayerRegistry layers = engine.GetService(CoreServiceKeys.VisionFogLayerRegistry)
            ?? throw new InvalidOperationException("FogMobaTerrain requires VisionFogLayerRegistry.");
        FogFieldStore fields = engine.GetService(CoreServiceKeys.VisionFogFieldStore)
            ?? throw new InvalidOperationException("FogMobaTerrain requires VisionFogFieldStore.");
        _cellMap = engine.GetService(CoreServiceKeys.VisionFogCellMap)
            ?? throw new InvalidOperationException("FogMobaTerrain requires VisionFogCellMap.");
        _groundLayer = layers.GetId("moba-ground");
        if (_groundLayer.Value == 0) _groundLayer = layers.Register("moba-ground", CellSizeCm, 10);
        _groundMask = layers.ToMask(_groundLayer);
        ApplyTerrainRules();
        EnsureEntities(engine.World);
        fields.GetOrCreate(ScopeKey, layers.Get(_groundLayer));
        if (reset)
        {
            _tick = 0;
            _rangeCm = 5200;
            _cone = true;
            _rulesEnabled = true;
            _memoryEnabled = true;
            SeedExplored(fields.GetOrCreate(ScopeKey, layers.Get(_groundLayer)));
        }
    }

    private void EnsureEntities(World world)
    {
        if (!world.IsAlive(_viewer))
        {
            _viewer = world.Create(
                WorldPositionCm.FromCm(-6500, 0),
                new FacingDirection { AngleRad = 0f },
                new VisionEmitterCm { ScopeKeyId = ScopeKey, LayerMask = _groundMask, Polarity = VisionPolarity.Reveal, Aperture = VisionAperture.Cone(_rangeCm, 48), DetectionStrength = 1 },
                new Name { Value = "Fog Moba Observer" });
        }
        EnsureContact(world, 0, -1000, 0, "North lane contact");
        EnsureContact(world, 1, 1400, -1800, "River brush contact");
        EnsureContact(world, 2, 5200, 2400, "South lane contact");
    }

    private void EnsureContact(World world, int index, int x, int y, string name)
    {
        if (!world.IsAlive(_contacts[index]))
        {
            _contacts[index] = world.Create(WorldPositionCm.FromCm(x, y), new Name { Value = name }, new FogOccupantCm { ExposeLayerMask = _groundMask, StealthLevel = index == 1 ? (byte)1 : (byte)0 });
        }
    }

    private void ApplyTerrainRules()
    {
        if (_cellMap == null) return;
        _wallCells = 0;
        _brushCells = 0;
        for (int y = -10; y <= 10; y++)
        {
            _cellMap.SetOpaque(new FogCell(0, y), _rulesEnabled);
            _cellMap.SetOpaque(new FogCell(1, y), _rulesEnabled && y is >= -5 and <= 5);
            if (_rulesEnabled) _wallCells++;
            if (_rulesEnabled && y is >= -5 and <= 5) _wallCells++;
        }
        for (int x = -7; x <= 7; x++)
        {
            _cellMap.SetConcealed(new FogCell(x, -4), _rulesEnabled);
            _cellMap.SetConcealed(new FogCell(x, 4), _rulesEnabled);
            if (_rulesEnabled) _brushCells += 2;
        }
        for (int x = -4; x <= 4; x++) _cellMap.SetHeightTier(new FogCell(x, 1), _rulesEnabled ? 2 : 0);
    }

    private static void SeedExplored(FogField field)
    {
        for (int y = -8; y <= 8; y++) for (int x = -12; x <= -8; x++) field.SetExplored(new FogCell(x, y));
        field.MarkDirtyRegion(new IntRect(-13, -9, 26, 18));
    }

    private void ClearExplored(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.VisionFogFieldStore) is not FogFieldStore fields ||
            !fields.TryGet(ScopeKey, _groundLayer, out FogField field)) return;
        Span<FogCellState> cells = stackalloc FogCellState[512];
        int count = field.CopyCells(cells);
        for (int i = 0; i < count; i++) if (cells[i].Visibility == CellVisibility.Explored) field.SetVisibility(cells[i].Cell, CellVisibility.Unseen);
    }

    private void RefreshSnapshot(GameEngine engine)
    {
        if (!engine.World.IsAlive(_viewer)) return;
        ref WorldPositionCm position = ref engine.World.Get<WorldPositionCm>(_viewer);
        ref FacingDirection facing = ref engine.World.Get<FacingDirection>(_viewer);
        int visible = 0, explored = 0;
        if (engine.GetService(CoreServiceKeys.VisionFogFieldStore) is FogFieldStore fields && fields.TryGet(ScopeKey, _groundLayer, out FogField field))
        {
            Span<FogCellState> cells = stackalloc FogCellState[1024];
            int count = field.CopyCells(cells);
            for (int i = 0; i < count; i++) if (cells[i].Visibility == CellVisibility.Visible) visible++; else if (cells[i].Visibility == CellVisibility.Explored) explored++;
        }
        WorldCmInt2 current = position.ToWorldCmInt2();
        const int terrainCellCount = 25 * 17;
        int unseen = Math.Max(0, terrainCellCount - visible - explored);
        Snapshot = new FogMobaTerrainSnapshot(_tick, current.X, current.Y, (int)MathF.Round(WorldPlane2D.NormalizeDegreesPositive(facing.AngleRad * 57.29578f)), _cone ? "Cone" : "Disk", _rangeCm, _rulesEnabled, _memoryEnabled, visible, explored, unseen, _wallCells, _brushCells, _status);
    }

    private static bool IsMap(GameEngine engine) => engine.CurrentMapSession?.MapId.Value == FogMobaTerrainIds.MapId;
    private void ActivateInput(PlayerInputHandler? input) { if (input != null && input.HasContext("FogMobaTerrainShowcase.Controls")) input.PushContext("FogMobaTerrainShowcase.Controls"); }
    private void DeactivateInput(PlayerInputHandler? input) { input?.PopContext("FogMobaTerrainShowcase.Controls"); }
    private void Destroy(World world) { if (world.IsAlive(_viewer)) world.Destroy(_viewer); _viewer = Entity.Null; for (int i = 0; i < _contacts.Length; i++) { if (world.IsAlive(_contacts[i])) world.Destroy(_contacts[i]); _contacts[i] = Entity.Null; } }
}
