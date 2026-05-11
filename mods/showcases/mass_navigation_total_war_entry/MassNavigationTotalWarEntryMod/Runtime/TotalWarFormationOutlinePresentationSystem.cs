using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal sealed class TotalWarFormationOutlinePresentationSystem : ISystem<float>
{
    private const int OverlayStableIdStride = 32;
    private const int RectangleFrontSegmentIndex = 1;
    private const int RectangleBackSegmentIndex = 2;
    private const int RectangleLeftSegmentIndex = 3;
    private const int RectangleRightSegmentIndex = 4;
    private const int CircleRingSegmentIndex = 1;
    private const int FrontIndicatorSegmentIndex = 16;

    private static readonly QueryDescription FormationOutlineQuery = new QueryDescription()
        .WithAll<TotalWarFormationAnchor, TotalWarFormationState, TotalWarFormationOutline, VisualTransform, PresentationStableId>();

    private readonly GameEngine _engine;
    private readonly TotalWarShowcaseRuntime _runtime;
    private readonly GroundOverlayBuffer _overlays;
    private readonly List<int> _currentStableIds = new();
    private readonly List<int> _previousStableIds = new();

    public TotalWarFormationOutlinePresentationSystem(GameEngine engine, TotalWarShowcaseRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
            ?? throw new InvalidOperationException("Total War formation outline requires GroundOverlayBuffer.");
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_runtime.IsCurrentShowcaseMap(_engine))
        {
            RemovePreviousOverlays();
            return;
        }

        _currentStableIds.Clear();
        int emitted = 0;
        _engine.World.Query(
            in FormationOutlineQuery,
            (Entity entity, ref TotalWarFormationAnchor _, ref TotalWarFormationState state, ref TotalWarFormationOutline outline, ref VisualTransform transform, ref PresentationStableId stableId) =>
            {
                emitted += EmitOutline(entity, in state, in outline, in transform, stableId.Value);
            });

        RemoveStaleOverlays();
        _engine.GlobalContext["MassNavigation.TotalWar.FormationOutlineCount"] = emitted;
    }

    private int EmitOutline(
        Entity entity,
        in TotalWarFormationState state,
        in TotalWarFormationOutline outline,
        in VisualTransform transform,
        int ownerStableId)
    {
        if (ownerStableId <= 0)
        {
            throw new InvalidOperationException("Total War formation outline requires a positive PresentationStableId.");
        }

        return outline.Shape switch
        {
            TotalWarFormationOutlineShape.Rectangle => EmitRectangle(entity, ownerStableId, in state, in outline, in transform),
            TotalWarFormationOutlineShape.Circle => EmitCircle(entity, ownerStableId, in state, in outline, in transform),
            _ => throw new InvalidOperationException($"Total War formation outline has unsupported shape {outline.Shape}."),
        };
    }

    private int EmitRectangle(
        Entity entity,
        int ownerStableId,
        in TotalWarFormationState state,
        in TotalWarFormationOutline outline,
        in VisualTransform transform)
    {
        float widthM = WorldUnits.CmToM(outline.WidthCm);
        float depthM = WorldUnits.CmToM(outline.DepthCm);
        float edgeWidthM = WorldUnits.CmToM(outline.EdgeLineWidthCm);
        Vector3 center = ResolveCenter(in transform, outline.HeightOffsetM);
        Vector2 forward = ResolveForward(state.FacingRad);
        Vector2 lateral = new(-forward.Y, forward.X);
        Vector3 forward3 = ToVisualVector(forward);
        Vector3 lateral3 = ToVisualVector(lateral);
        Vector3 frontCenter = center + (forward3 * (depthM * 0.5f));
        Vector3 backCenter = center - (forward3 * (depthM * 0.5f));
        Vector3 leftCenter = center - (lateral3 * (widthM * 0.5f));
        Vector3 rightCenter = center + (lateral3 * (widthM * 0.5f));

        int count = 0;
        count += AddLine(entity, ownerStableId, RectangleFrontSegmentIndex, frontCenter - (lateral3 * (widthM * 0.5f)), lateral, widthM, edgeWidthM, in outline);
        count += AddLine(entity, ownerStableId, RectangleBackSegmentIndex, backCenter - (lateral3 * (widthM * 0.5f)), lateral, widthM, edgeWidthM, in outline);
        count += AddLine(entity, ownerStableId, RectangleLeftSegmentIndex, leftCenter - (forward3 * (depthM * 0.5f)), forward, depthM, edgeWidthM, in outline);
        count += AddLine(entity, ownerStableId, RectangleRightSegmentIndex, rightCenter - (forward3 * (depthM * 0.5f)), forward, depthM, edgeWidthM, in outline);
        count += EmitFrontIndicator(entity, ownerStableId, center, forward, in outline);
        return count;
    }

    private int EmitCircle(
        Entity entity,
        int ownerStableId,
        in TotalWarFormationState state,
        in TotalWarFormationOutline outline,
        in VisualTransform transform)
    {
        float radiusM = WorldUnits.CmToM(outline.RadiusCm);
        float ringWidthM = WorldUnits.CmToM(outline.CircleRingWidthCm);
        if (radiusM <= 0f || ringWidthM <= 0f)
        {
            throw new InvalidOperationException("Total War circle formation outline requires positive radius and ring width.");
        }

        float innerRadius = MathF.Max(0f, radiusM - ringWidthM);
        Vector3 center = ResolveCenter(in transform, outline.HeightOffsetM);
        int stableId = CreateOverlayStableId(ownerStableId, CircleRingSegmentIndex);
        var item = new GroundOverlayItem
        {
            StableId = stableId,
            Shape = GroundOverlayShape.Ring,
            Center = center,
            Radius = radiusM,
            InnerRadius = innerRadius,
            FillColor = outline.FillColor,
            BorderColor = outline.BorderColor,
            BorderWidth = ringWidthM,
        };

        if (!_overlays.Upsert(in item))
        {
            throw new InvalidOperationException("GroundOverlayBuffer overflowed while emitting Total War circle formation outline.");
        }

        _currentStableIds.Add(stableId);
        int count = 1;
        count += EmitFrontIndicator(entity, ownerStableId, center, ResolveForward(state.FacingRad), in outline);
        return count;
    }

    private int EmitFrontIndicator(
        Entity entity,
        int ownerStableId,
        Vector3 center,
        Vector2 forward,
        in TotalWarFormationOutline outline)
    {
        float lengthM = WorldUnits.CmToM(outline.FrontIndicatorLengthCm);
        if (!(lengthM > 0f))
        {
            return 0;
        }

        float widthM = WorldUnits.CmToM(outline.FrontIndicatorLineWidthCm);
        Vector3 start = center;
        return AddLine(entity, ownerStableId, FrontIndicatorSegmentIndex, start, forward, lengthM, widthM, in outline);
    }

    private int AddLine(
        Entity entity,
        int ownerStableId,
        int segmentIndex,
        Vector3 start,
        Vector2 direction,
        float lengthM,
        float widthM,
        in TotalWarFormationOutline outline)
    {
        if (lengthM <= 0f || widthM <= 0f)
        {
            throw new InvalidOperationException("Total War formation outline line segments require positive length and width.");
        }

        int stableId = CreateOverlayStableId(ownerStableId, segmentIndex);
        var item = new GroundOverlayItem
        {
            StableId = stableId,
            Shape = GroundOverlayShape.Line,
            Center = start,
            Rotation = MathF.Atan2(direction.Y, direction.X),
            Length = lengthM,
            Width = widthM,
            FillColor = outline.FillColor,
            BorderColor = outline.BorderColor,
            BorderWidth = widthM,
        };

        if (!_overlays.Upsert(in item))
        {
            throw new InvalidOperationException($"GroundOverlayBuffer overflowed while emitting Total War formation outline for entity {entity.Id}.");
        }

        _currentStableIds.Add(stableId);
        return 1;
    }

    private void RemovePreviousOverlays()
    {
        for (int i = 0; i < _previousStableIds.Count; i++)
        {
            _overlays.Remove(_previousStableIds[i]);
        }

        _previousStableIds.Clear();
        _currentStableIds.Clear();
    }

    private void RemoveStaleOverlays()
    {
        for (int i = 0; i < _previousStableIds.Count; i++)
        {
            int stableId = _previousStableIds[i];
            if (!_currentStableIds.Contains(stableId))
            {
                _overlays.Remove(stableId);
            }
        }

        _previousStableIds.Clear();
        _previousStableIds.AddRange(_currentStableIds);
    }

    private static int CreateOverlayStableId(int ownerStableId, int segmentIndex)
    {
        long stableId = ((long)ownerStableId * OverlayStableIdStride) + segmentIndex;
        if (stableId > int.MaxValue)
        {
            throw new InvalidOperationException($"Total War formation outline stable id overflow for owner {ownerStableId}.");
        }

        return (int)stableId;
    }

    private static Vector3 ResolveCenter(in VisualTransform transform, float heightOffsetM)
    {
        return new Vector3(transform.Position.X, transform.Position.Y + heightOffsetM, transform.Position.Z);
    }

    private static Vector2 ResolveForward(float facingRad)
    {
        return Vector2.Normalize(new Vector2(MathF.Cos(facingRad), MathF.Sin(facingRad)));
    }

    private static Vector3 ToVisualVector(in Vector2 logicVector)
    {
        return new Vector3(logicVector.X, 0f, logicVector.Y);
    }

}
