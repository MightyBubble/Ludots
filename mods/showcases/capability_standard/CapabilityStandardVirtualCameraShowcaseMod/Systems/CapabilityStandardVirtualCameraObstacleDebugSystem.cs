using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Scripting;
using Ludots.Core.StructureCollision;

namespace CapabilityStandardVirtualCameraShowcaseMod.Systems;

internal sealed class CapabilityStandardVirtualCameraObstacleDebugSystem : ISystem<float>
{
    private readonly GameEngine _engine;

    public CapabilityStandardVirtualCameraObstacleDebugSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!CapabilityStandardVirtualCameraShowcaseIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        DebugDrawCommandBuffer debugDraw = _engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer)
            ?? throw new InvalidOperationException("Capability standard virtual camera obstacle debug requires DebugDrawCommandBuffer.");
        StructureCollisionAsset asset = _engine.GetService(CoreServiceKeys.StructureCollisionAsset)
            ?? throw new InvalidOperationException("Capability standard virtual camera obstacle debug requires StructureCollisionAsset.");

        DrawVisionBlockers(debugDraw, asset);
        DrawCameraBoom(debugDraw);
    }

    private static void DrawVisionBlockers(DebugDrawCommandBuffer debugDraw, StructureCollisionAsset asset)
    {
        for (int surfaceIndex = 0; surfaceIndex < asset.SurfaceCount; surfaceIndex++)
        {
            if ((asset.Surfaces.Flags[surfaceIndex] & StructureSurfaceFlags.BlocksVision) == 0)
            {
                continue;
            }

            int shapeIndex = asset.Surfaces.ShapeRefs[surfaceIndex];
            if (asset.Shapes.Kinds[shapeIndex] != StructureShapeKind.WallSegment)
            {
                DrawSurfaceBounds(debugDraw, asset.Surfaces.Bounds[surfaceIndex], DebugDrawColor.Yellow);
                continue;
            }

            debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = new Vector2(
                    WorldUnits.CmToM(asset.Shapes.SegmentAXCm[shapeIndex]),
                    WorldUnits.CmToM(asset.Shapes.SegmentAZCm[shapeIndex])),
                B = new Vector2(
                    WorldUnits.CmToM(asset.Shapes.SegmentBXCm[shapeIndex]),
                    WorldUnits.CmToM(asset.Shapes.SegmentBZCm[shapeIndex])),
                Thickness = MathF.Max(0.02f, WorldUnits.CmToM(asset.Shapes.SegmentHalfWidthCm[shapeIndex])),
                Color = DebugDrawColor.Yellow
            });
        }
    }

    private void DrawCameraBoom(DebugDrawCommandBuffer debugDraw)
    {
        CameraState state = _engine.GameSession.Camera.State;
        CameraRenderState3D render = CameraViewportUtil.StateToRenderState(state);
        Vector2 target = new(render.Target.X, render.Target.Z);
        Vector2 camera = new(render.Position.X, render.Position.Z);
        bool obstructed = state.CameraCollisionCorrectionCm > 0.01f;
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = target,
            B = camera,
            Thickness = 0.025f,
            Color = obstructed ? new DebugDrawColor(255, 120, 50) : new DebugDrawColor(80, 220, 255)
        });
        debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = camera,
            Radius = obstructed ? 0.28f : 0.18f,
            Thickness = 0.02f,
            Color = obstructed ? new DebugDrawColor(255, 120, 50) : new DebugDrawColor(80, 220, 255)
        });
    }

    private static void DrawSurfaceBounds(DebugDrawCommandBuffer debugDraw, WorldAabbCm bounds, DebugDrawColor color)
    {
        debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(
                WorldUnits.CmToM(bounds.Left + (bounds.Width * 0.5f)),
                WorldUnits.CmToM(bounds.Top + (bounds.Height * 0.5f))),
            HalfWidth = WorldUnits.CmToM(bounds.Width * 0.5f),
            HalfHeight = WorldUnits.CmToM(bounds.Height * 0.5f),
            RotationRadians = 0f,
            Thickness = 0.02f,
            Color = color
        });
    }
}
