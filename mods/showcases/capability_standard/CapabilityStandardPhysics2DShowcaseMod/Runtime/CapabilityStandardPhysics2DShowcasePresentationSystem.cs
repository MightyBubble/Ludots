using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace CapabilityStandardPhysics2DShowcaseMod.Runtime;

internal sealed class CapabilityStandardPhysics2DShowcasePresentationSystem : ISystem<float>
{
    private static readonly QueryDescription DynamicQuery = new QueryDescription()
        .WithAll<CapabilityStandardPhysics2DShowcaseDynamicTag, Position2D, Collider2D>();

    private static readonly QueryDescription StaticColliderQuery = new QueryDescription()
        .WithAll<CapabilityStandardPhysics2DShowcaseStaticObstacleTag, Position2D, Collider2D, Mass2D>();

    private static readonly QueryDescription StaticCompoundQuery = new QueryDescription()
        .WithAll<CapabilityStandardPhysics2DShowcaseStaticObstacleTag, Position2D, CompoundObstacle2DState, Mass2D>();

    private static readonly Vector4 AxisXColor = new(0.93f, 0.24f, 0.24f, 0.95f);
    private static readonly Vector4 AxisZColor = new(0.20f, 0.55f, 0.96f, 0.95f);
    private static readonly Vector4 DynamicColor = new(0.22f, 0.95f, 0.54f, 1f);
    private static readonly Vector4 StaticColor = new(0.42f, 0.68f, 1f, 0.92f);
    private static readonly Vector4 PolygonColor = new(0.97f, 0.72f, 0.24f, 0.96f);
    private static readonly Vector4 DraftColor = new(1f, 0.92f, 0.35f, 0.90f);

    private const int BaselineStableIdBase = 100_000_000;
    private const int DynamicStableIdBase = 300_000_000;
    private const int StaticStableIdBase = 500_000_000;
    private const int DraftStableIdBase = 700_000_000;
    private const float BaselineY = 0.28f;

    private readonly GameEngine _engine;
    private readonly CapabilityStandardPhysics2DShowcaseRuntime _runtime;
    private readonly CapabilityStandardPhysics2DShowcasePanelController _panel;
    private readonly WorldCmInt2[] _polygonDraftScratch = new WorldCmInt2[ManifestationObstaclePolygon2D.MaxVertices];
    private int _cubeMeshId;
    private int _sphereMeshId;

    public CapabilityStandardPhysics2DShowcasePresentationSystem(
        GameEngine engine,
        CapabilityStandardPhysics2DShowcaseRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _panel = new CapabilityStandardPhysics2DShowcasePanelController(runtime);
    }

    public void Initialize()
    {
        if (_engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) is MeshAssetRegistry meshes)
        {
            _cubeMeshId = meshes.GetId(WellKnownMeshKeys.Cube);
            _sphereMeshId = meshes.GetId(WellKnownMeshKeys.Sphere);
        }
    }

    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        if (_cubeMeshId <= 0 || _sphereMeshId <= 0)
        {
            Initialize();
        }

        if (!_runtime.IsActive)
        {
            if (_engine.GetService(CoreServiceKeys.UIRoot) is UIRoot inactiveRoot)
            {
                _panel.ClearIfOwned(inactiveRoot);
            }

            return;
        }

        CapabilityStandardPhysics2DShowcasePanelState state = _runtime.CapturePanelState(_engine);
        EmitScenePresentation();
        if (_engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panel.MountOrSync(root, _engine, in state);
        }
    }

    private void EmitScenePresentation()
    {
        if (_engine.GetService(CoreServiceKeys.PresentationRequestBuffer) is not PresentationRequestBuffer requests)
        {
            return;
        }

        EmitBaseline(requests);
        EmitDynamicBodies(requests);
        EmitStaticColliderBodies(requests);
        EmitStaticCompoundBodies(requests);
        EmitPolygonDraft(requests);
    }

    private void EmitBaseline(PresentationRequestBuffer requests)
    {
        Vector2 cameraTargetCm = _engine.GameSession.Camera.State.TargetCm;
        Vector3 center = WorldUnits.WorldCmToVisualMeters(cameraTargetCm.X, cameraTargetCm.Y, 0f);

        AddProxy(
            requests,
            Entity.Null,
            _cubeMeshId,
            BaselineStableIdBase + 1,
            center + new Vector3(0f, BaselineY, 0f),
            Quaternion.Identity,
            new Vector3(28f, 0.12f, 18f),
            new Vector4(0.07f, 0.22f, 0.28f, 0.86f),
            VisualMobility.Movable);

        AddProxy(
            requests,
            Entity.Null,
            _cubeMeshId,
            BaselineStableIdBase + 2,
            center + new Vector3(0f, BaselineY + 0.09f, 0f),
            Quaternion.Identity,
            new Vector3(28f, 0.12f, 0.18f),
            AxisXColor,
            VisualMobility.Movable);

        AddProxy(
            requests,
            Entity.Null,
            _cubeMeshId,
            BaselineStableIdBase + 3,
            center + new Vector3(0f, BaselineY + 0.18f, 0f),
            Quaternion.Identity,
            new Vector3(0.18f, 0.12f, 18f),
            AxisZColor,
            VisualMobility.Movable);

        AddProxy(
            requests,
            Entity.Null,
            _sphereMeshId,
            BaselineStableIdBase + 4,
            center + new Vector3(0f, BaselineY + 0.70f, 0f),
            Quaternion.Identity,
            new Vector3(1.25f),
            PolygonColor,
            VisualMobility.Movable);

        EmitLineSegment(
            requests,
            Entity.Null,
            BaselineStableIdBase + 5,
            center + new Vector3(-14f, BaselineY + 0.34f, -9f),
            center + new Vector3(14f, BaselineY + 0.34f, -9f),
            StaticColor);
        EmitLineSegment(
            requests,
            Entity.Null,
            BaselineStableIdBase + 6,
            center + new Vector3(14f, BaselineY + 0.34f, -9f),
            center + new Vector3(14f, BaselineY + 0.34f, 9f),
            StaticColor);
        EmitLineSegment(
            requests,
            Entity.Null,
            BaselineStableIdBase + 7,
            center + new Vector3(14f, BaselineY + 0.34f, 9f),
            center + new Vector3(-14f, BaselineY + 0.34f, 9f),
            StaticColor);
        EmitLineSegment(
            requests,
            Entity.Null,
            BaselineStableIdBase + 8,
            center + new Vector3(-14f, BaselineY + 0.34f, 9f),
            center + new Vector3(-14f, BaselineY + 0.34f, -9f),
            StaticColor);

        AddProxy(
            requests,
            Entity.Null,
            _cubeMeshId,
            BaselineStableIdBase + 9,
            center + new Vector3(-3.4f, BaselineY + 0.36f, -2.2f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.35f),
            new Vector3(1.5f, 0.72f, 0.8f),
            StaticColor,
            VisualMobility.Movable);

        AddProxy(
            requests,
            Entity.Null,
            _cubeMeshId,
            BaselineStableIdBase + 10,
            center + new Vector3(3.1f, BaselineY + 0.36f, 2.0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.55f),
            new Vector3(1.1f, 0.72f, 1.35f),
            StaticColor,
            VisualMobility.Movable);

        AddProxy(
            requests,
            Entity.Null,
            _sphereMeshId,
            BaselineStableIdBase + 11,
            center + new Vector3(-1.55f, BaselineY + 0.72f, 1.45f),
            Quaternion.Identity,
            new Vector3(0.9f),
            DynamicColor,
            VisualMobility.Movable);

        AddProxy(
            requests,
            Entity.Null,
            _sphereMeshId,
            BaselineStableIdBase + 12,
            center + new Vector3(1.55f, BaselineY + 0.72f, -1.35f),
            Quaternion.Identity,
            new Vector3(0.9f),
            DynamicColor,
            VisualMobility.Movable);
    }

    private void EmitDynamicBodies(PresentationRequestBuffer requests)
    {
        foreach (ref var chunk in _engine.World.Query(in DynamicQuery))
        {
            Span<Position2D> positions = chunk.GetSpan<Position2D>();
            Span<Collider2D> colliders = chunk.GetSpan<Collider2D>();

            for (int i = 0; i < chunk.Count; i++)
            {
                Entity entity = chunk.Entity(i);
                Vector3 scale = ResolveColliderScale(in colliders[i], defaultScale: new Vector3(0.36f));
                AddProxy(
                    requests,
                    entity,
                    _sphereMeshId,
                    ComposeEntityStableId(DynamicStableIdBase, entity, 0),
                    ToVisualPosition(positions[i].Value, 0.28f),
                    Quaternion.Identity,
                    scale,
                    DynamicColor,
                    VisualMobility.Movable);
            }
        }
    }

    private void EmitStaticColliderBodies(PresentationRequestBuffer requests)
    {
        foreach (ref var chunk in _engine.World.Query(in StaticColliderQuery))
        {
            Span<Position2D> positions = chunk.GetSpan<Position2D>();
            Span<Collider2D> colliders = chunk.GetSpan<Collider2D>();

            for (int i = 0; i < chunk.Count; i++)
            {
                EmitColliderShape(
                    requests,
                    chunk.Entity(i),
                    positions[i].Value,
                    colliders[i],
                    ComposeEntityStableId(StaticStableIdBase, chunk.Entity(i), 0),
                    StaticColor);
            }
        }
    }

    private void EmitStaticCompoundBodies(PresentationRequestBuffer requests)
    {
        foreach (ref var chunk in _engine.World.Query(in StaticCompoundQuery))
        {
            Span<Position2D> positions = chunk.GetSpan<Position2D>();
            Span<CompoundObstacle2DState> states = chunk.GetSpan<CompoundObstacle2DState>();

            for (int i = 0; i < chunk.Count; i++)
            {
                Entity entity = chunk.Entity(i);
                ref CompoundObstacle2DState state = ref states[i];
                if (state.SinkPhysicsCollider == 0)
                {
                    continue;
                }

                AddProxy(
                    requests,
                    entity,
                    _cubeMeshId,
                    ComposeEntityStableId(StaticStableIdBase, entity, 1),
                    ToVisualPosition(positions[i].Value, 0.20f),
                    Quaternion.Identity,
                    ResolveCompoundScale(in state),
                    StaticColor,
                    VisualMobility.Movable);
            }
        }
    }

    private void EmitPolygonDraft(PresentationRequestBuffer requests)
    {
        int count = _runtime.CopyPolygonDraftVertices(_polygonDraftScratch);
        for (int i = 0; i < count; i++)
        {
            Vector3 position = WorldUnits.WorldCmToVisualMeters(_polygonDraftScratch[i], yMeters: 0.34f);
            AddProxy(
                requests,
                Entity.Null,
                _sphereMeshId,
                DraftStableIdBase + i,
                position,
                Quaternion.Identity,
                new Vector3(0.24f),
                DraftColor,
                VisualMobility.Movable);

            if (i > 0)
            {
                EmitLineSegment(
                    requests,
                    Entity.Null,
                    DraftStableIdBase + 32 + i,
                    _polygonDraftScratch[i - 1],
                    _polygonDraftScratch[i],
                    DraftColor);
            }
        }
    }

    private void EmitColliderShape(
        PresentationRequestBuffer requests,
        Entity owner,
        in Fix64Vec2 position,
        in Collider2D collider,
        int stableId,
        in Vector4 color)
    {
        switch (collider.Type)
        {
            case ColliderType2D.Circle:
                if (!ShapeDataStorage2D.TryGetCircle(collider.ShapeDataIndex, out CircleShapeData circle))
                {
                    return;
                }

                AddProxy(
                    requests,
                    owner,
                    _sphereMeshId,
                    stableId,
                    ToVisualPosition(position + circle.LocalCenter, 0.24f),
                    Quaternion.Identity,
                    new Vector3(MathF.Max(0.12f, WorldUnits.CmToM(circle.Radius.ToFloat()) * 2f), 0.22f, MathF.Max(0.12f, WorldUnits.CmToM(circle.Radius.ToFloat()) * 2f)),
                    color,
                    VisualMobility.Movable);
                break;

            case ColliderType2D.Box:
                if (!ShapeDataStorage2D.TryGetBox(collider.ShapeDataIndex, out BoxShapeData box))
                {
                    return;
                }

                AddProxy(
                    requests,
                    owner,
                    _cubeMeshId,
                    stableId,
                    ToVisualPosition(position + box.LocalCenter, 0.20f),
                    Quaternion.Identity,
                    new Vector3(
                        MathF.Max(0.12f, WorldUnits.CmToM(box.HalfWidth.ToFloat()) * 2f),
                        0.34f,
                        MathF.Max(0.12f, WorldUnits.CmToM(box.HalfHeight.ToFloat()) * 2f)),
                    color,
                    VisualMobility.Movable);
                break;

            case ColliderType2D.Polygon:
                if (!ShapeDataStorage2D.TryGetPolygon(collider.ShapeDataIndex, out PolygonShapeData polygon) ||
                    polygon.Vertices == null ||
                    polygon.VertexCount < 3)
                {
                    return;
                }

                EmitPolygonMarker(requests, owner, stableId, position, in polygon, color);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(collider.Type));
        }
    }

    private void EmitPolygonMarker(
        PresentationRequestBuffer requests,
        Entity owner,
        int stableId,
        in Fix64Vec2 position,
        in PolygonShapeData polygon,
        in Vector4 color)
    {
        Vector3 center = ToVisualPosition(position + polygon.LocalOffset, 0.26f);
        AddProxy(
            requests,
            owner,
            _sphereMeshId,
            stableId,
            center,
            Quaternion.Identity,
            new Vector3(0.34f),
            PolygonColor,
            VisualMobility.Movable);

        for (int vertexIndex = 0; vertexIndex < polygon.VertexCount; vertexIndex++)
        {
            Fix64Vec2 aLocal = polygon.LocalOffset + polygon.Vertices[vertexIndex] - polygon.LocalCenter;
            Fix64Vec2 bLocal = polygon.LocalOffset + polygon.Vertices[(vertexIndex + 1) % polygon.VertexCount] - polygon.LocalCenter;
            EmitLineSegment(
                requests,
                owner,
                stableId + 64 + vertexIndex,
                (position + aLocal).ToWorldCmInt2(),
                (position + bLocal).ToWorldCmInt2(),
                color);
        }
    }

    private void EmitLineSegment(
        PresentationRequestBuffer requests,
        Entity owner,
        int stableId,
        in WorldCmInt2 from,
        in WorldCmInt2 to,
        in Vector4 color)
    {
        Vector3 a = WorldUnits.WorldCmToVisualMeters(from, yMeters: BaselineY + 0.34f);
        Vector3 b = WorldUnits.WorldCmToVisualMeters(to, yMeters: BaselineY + 0.34f);
        EmitLineSegment(requests, owner, stableId, in a, in b, color);
    }

    private void EmitLineSegment(
        PresentationRequestBuffer requests,
        Entity owner,
        int stableId,
        in Vector3 a,
        in Vector3 b,
        in Vector4 color)
    {
        Vector3 delta = b - a;
        float length = MathF.Sqrt((delta.X * delta.X) + (delta.Z * delta.Z));
        if (length <= 0.001f)
        {
            return;
        }

        float yaw = MathF.Atan2(delta.X, delta.Z);
        AddProxy(
            requests,
            owner,
            _cubeMeshId,
            stableId,
            (a + b) * 0.5f,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw),
            new Vector3(0.16f, 0.20f, length),
            color,
            VisualMobility.Movable);
    }

    private static void AddProxy(
        PresentationRequestBuffer requests,
        Entity owner,
        int meshAssetId,
        int stableId,
        in Vector3 position,
        in Quaternion rotation,
        in Vector3 scale,
        in Vector4 color,
        VisualMobility mobility)
    {
        if (meshAssetId <= 0)
        {
            return;
        }

        var proxy = new PresentationVisualProxy
        {
            ProxyKind = PresentationVisualProxyKind.Entity,
            MeshAssetId = meshAssetId,
            Position = position,
            Rotation = rotation,
            Scale = scale,
            Color = color,
            StableId = stableId,
            RenderPath = VisualRenderPath.StaticMesh,
            AssetKind = AssetKind.Mesh,
            Mobility = mobility,
            Flags = VisualRuntimeFlags.Visible,
            Visibility = VisualVisibility.Visible,
            LOD = LODLevel.High,
        };

        requests.Add(PresentationRequest.FromVisualProxy(owner, in proxy));
    }

    private static Vector3 ResolveColliderScale(in Collider2D collider, in Vector3 defaultScale)
    {
        if (collider.Type == ColliderType2D.Circle &&
            ShapeDataStorage2D.TryGetCircle(collider.ShapeDataIndex, out CircleShapeData circle))
        {
            float diameterMeters = MathF.Max(0.12f, WorldUnits.CmToM(circle.Radius.ToFloat()) * 2f);
            return new Vector3(diameterMeters, diameterMeters, diameterMeters);
        }

        return defaultScale;
    }

    private static int ComposeEntityStableId(int baseId, Entity entity, int slot)
    {
        unchecked
        {
            int id = baseId + ((entity.Id & 0x3FFFF) * 128) + slot;
            return id > 0 ? id : baseId + slot;
        }
    }

    private static Vector3 ResolveCompoundScale(in CompoundObstacle2DState state)
    {
        float maxRadiusMeters = 0.6f;
        for (int pieceIndex = 0; pieceIndex < state.PieceCount; pieceIndex++)
        {
            switch (state.GetShape(pieceIndex))
            {
                case ManifestationObstacleShape2D.Circle:
                    if (ShapeDataStorage2D.TryGetCircle(state.GetShapeDataIndex(pieceIndex), out CircleShapeData circle))
                    {
                        Vector2 center = circle.LocalCenter.ToVector2();
                        maxRadiusMeters = MathF.Max(
                            maxRadiusMeters,
                            WorldUnits.CmToM(center.Length() + circle.Radius.ToFloat()));
                    }
                    break;

                case ManifestationObstacleShape2D.Box:
                    if (ShapeDataStorage2D.TryGetBox(state.GetShapeDataIndex(pieceIndex), out BoxShapeData box))
                    {
                        Vector2 center = box.LocalCenter.ToVector2();
                        float radiusCm = center.Length() + MathF.Sqrt(
                            (box.HalfWidth.ToFloat() * box.HalfWidth.ToFloat()) +
                            (box.HalfHeight.ToFloat() * box.HalfHeight.ToFloat()));
                        maxRadiusMeters = MathF.Max(maxRadiusMeters, WorldUnits.CmToM(radiusCm));
                    }
                    break;

                case ManifestationObstacleShape2D.Polygon:
                    if (ShapeDataStorage2D.TryGetPolygon(state.GetShapeDataIndex(pieceIndex), out PolygonShapeData polygon) &&
                        polygon.Vertices != null)
                    {
                        for (int vertexIndex = 0; vertexIndex < polygon.VertexCount; vertexIndex++)
                        {
                            Vector2 vertex = (polygon.LocalOffset + polygon.Vertices[vertexIndex] - polygon.LocalCenter).ToVector2();
                            maxRadiusMeters = MathF.Max(maxRadiusMeters, WorldUnits.CmToM(vertex.Length()));
                        }
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        float diameter = MathF.Max(0.45f, maxRadiusMeters * 2f);
        return new Vector3(diameter, 0.34f, diameter);
    }

    private static Vector3 ToVisualPosition(in Fix64Vec2 position, float yMeters)
    {
        return WorldUnits.WorldCmToVisualMeters(position, yMeters);
    }
}
