using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using DynamicNavBakeShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;

namespace DynamicNavBakeShowcaseMod.Systems;

internal sealed class DynamicNavBakeShowcasePresentationSystem : BaseSystem<World, float>
{
    private const string LocalPathKey = "dynamic_nav_bake.local_path";
    private const string CorridorPathKey = "dynamic_nav_bake.corridor_path";
    private const int MaxLocalSegments = 256;
    private const int MaxCorridorSegments = 512;

    private readonly GameEngine _engine;
    private readonly DynamicNavBakeShowcaseRuntime _runtime;
    private readonly DynamicNavBakeShowcaseActions _actions;
    private readonly DynamicNavBakeShowcaseRaylibAutoTimeline _autoTimeline = new();
    private readonly (string Key, int Scope)[] _localScopes = new (string, int)[MaxLocalSegments];
    private readonly (string Key, int Scope)[] _corridorScopes = new (string, int)[MaxCorridorSegments];
    private int _localScopeCount;
    private int _corridorScopeCount;
    private int _publishedPathRevision = -1;
    private int _publishedCorridorRevision = -1;

    public DynamicNavBakeShowcasePresentationSystem(
        GameEngine engine,
        DynamicNavBakeShowcaseRuntime runtime,
        DynamicNavBakeShowcaseActions actions)
        : base(engine.World)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public override void Update(in float dt)
    {
        // Auto timeline must run every active presentation update before the path/corridor early return.
        if (_runtime.IsActive)
        {
            _runtime.CompletePendingMapFocusPresentation(_engine);
            _autoTimeline.Update(_engine, _actions);
            EmitPlacementOverlay();
        }

        if (!_runtime.IsActive ||
            !PresentationWorldFactPublisher.TryCreate(_engine.GlobalContext, out PresentationWorldFactPublisher facts))
        {
            EndAllScopes(default);
            _publishedPathRevision = -1;
            _publishedCorridorRevision = -1;
            return;
        }

        bool pathChanged = _runtime.PresentationPathRevision != _publishedPathRevision;
        bool corridorChanged = _runtime.PresentationCorridorRevision != _publishedCorridorRevision;
        if (!pathChanged && !corridorChanged)
        {
            return;
        }

        DynamicNavBakeShowcasePresentationConfig presentation = _runtime.ActiveConfig.Presentation;
        Entity owner = Entity.Null;
        if (pathChanged)
        {
            EndScopes(facts, _localScopes, ref _localScopeCount);
            int segmentIndex = 0;
            EmitPolyline(
                facts,
                owner,
                LocalPathKey,
                _runtime.CurrentPathXcm,
                _runtime.CurrentPathZcm,
                presentation.PathOverlayY,
                presentation.LocalPathWidthMeters,
                presentation.LocalPathBorderWidthMeters,
                _localScopes,
                ref _localScopeCount,
                ref segmentIndex);
            _publishedPathRevision = _runtime.PresentationPathRevision;
        }

        if (corridorChanged)
        {
            EndScopes(facts, _corridorScopes, ref _corridorScopeCount);
            int segmentIndex = MaxLocalSegments;
            EmitCorridor(
                facts,
                owner,
                _runtime.CoarseCorridorWorldPoints,
                presentation.PathOverlayY,
                presentation.CorridorPathWidthMeters,
                presentation.CorridorPathBorderWidthMeters,
                _corridorScopes,
                ref _corridorScopeCount,
                ref segmentIndex);
            _publishedCorridorRevision = _runtime.PresentationCorridorRevision;
        }
    }

    private void EmitPlacementOverlay()
    {
        DynamicNavBakeEditTransaction edit = _runtime.EditTransaction;
        if (!_runtime.ConstructionMode || !edit.HasPreviewWorld)
        {
            return;
        }

        GroundOverlayBuffer overlays = _engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
            ?? throw new InvalidOperationException(
                "Dynamic NavBake placement preview requires the Core GroundOverlayBuffer.");
        bool staged = edit.HasStagedEdit;
        bool baking = edit.PlayerNavState == DynamicNavBakePlayerNavState.Baking;
        bool legal = edit.PreviewLegality == DynamicNavBakePlacementLegality.Legal;
        Vector4 fill = baking || staged
            ? new Vector4(0.95f, 0.55f, 0.16f, 0.20f)
            : legal
                ? new Vector4(0.14f, 0.82f, 0.52f, 0.18f)
                : new Vector4(0.92f, 0.20f, 0.18f, 0.20f);
        Vector4 border = baking || staged
            ? new Vector4(1.00f, 0.68f, 0.25f, 0.95f)
            : legal
                ? new Vector4(0.20f, 0.95f, 0.62f, 0.95f)
                : new Vector4(1.00f, 0.30f, 0.25f, 0.95f);
        int radiusCm = edit.Tool == DynamicNavBakeEditTool.Building
            ? _runtime.ActiveConfig.Gate.NavRadiusCm
            : _runtime.ActiveConfig.TerrainBrushHalfExtentCm;
        // Building obstacle is a filled circle (ManifestationObstacleIntent2D); preview must match that footprint.
        GroundOverlayShape shape = edit.Tool == DynamicNavBakeEditTool.Building
            ? GroundOverlayShape.Circle
            : GroundOverlayShape.Ring;
        float innerRadiusMeters = shape == GroundOverlayShape.Ring
            ? SpatialScaleDefaults.CentimetersToMeters(Math.Max(1, radiusCm - 25))
            : 0f;
        var item = new GroundOverlayItem
        {
            StableId = 0,
            Shape = shape,
            Center = ResolveGroundOverlayCenter(edit.PreviewXCm, edit.PreviewZCm),
            Radius = SpatialScaleDefaults.CentimetersToMeters(radiusCm),
            InnerRadius = innerRadiusMeters,
            FillColor = fill,
            BorderColor = border,
            BorderWidth = 0.08f
        };
        if (!overlays.TryAdd(in item))
        {
            throw new InvalidOperationException(
                $"Core GroundOverlayBuffer capacity {overlays.Capacity} exhausted by Dynamic NavBake placement preview.");
        }
    }

    private static void EmitCorridor(
        PresentationWorldFactPublisher facts,
        Entity owner,
        ReadOnlySpan<(int XCm, int ZCm)> points,
        float pathOverlayY,
        float widthMeters,
        float borderWidthMeters,
        (string Key, int Scope)[] scopes,
        ref int scopeCount,
        ref int segmentIndex)
    {
        if (points.Length < 2)
        {
            return;
        }

        int segmentLimit = Math.Min(points.Length - 1, scopes.Length);
        for (int i = 0; i < segmentLimit; i++)
        {
            Vector3 start = ToVisualMeters(points[i].XCm, points[i].ZCm, pathOverlayY);
            Vector3 end = ToVisualMeters(points[i + 1].XCm, points[i + 1].ZCm, pathOverlayY);
            int scope = PresentationWorldFactPublisher.ComposeScope(CorridorPathKey, owner, segmentIndex++);
            facts.PublishWorldSplineUpdated(
                CorridorPathKey,
                owner,
                scope,
                start,
                end,
                width: widthMeters,
                borderWidth: borderWidthMeters);
            scopes[scopeCount++] = (CorridorPathKey, scope);
        }
    }

    private static void EmitPolyline(
        PresentationWorldFactPublisher facts,
        Entity owner,
        string key,
        IReadOnlyList<int> pathXcm,
        IReadOnlyList<int> pathZcm,
        float pathOverlayY,
        float widthMeters,
        float borderWidthMeters,
        (string Key, int Scope)[] scopes,
        ref int scopeCount,
        ref int segmentIndex)
    {
        if (pathXcm.Count < 2 || pathZcm.Count != pathXcm.Count)
        {
            return;
        }

        int segmentLimit = Math.Min(pathXcm.Count - 1, scopes.Length);
        for (int i = 0; i < segmentLimit; i++)
        {
            Vector3 start = ToVisualMeters(pathXcm[i], pathZcm[i], pathOverlayY);
            Vector3 end = ToVisualMeters(pathXcm[i + 1], pathZcm[i + 1], pathOverlayY);
            int scope = PresentationWorldFactPublisher.ComposeScope(key, owner, segmentIndex++);
            facts.PublishWorldSplineUpdated(
                key,
                owner,
                scope,
                start,
                end,
                width: widthMeters,
                borderWidth: borderWidthMeters);
            scopes[scopeCount++] = (key, scope);
        }
    }

    private void EndAllScopes(PresentationWorldFactPublisher facts)
    {
        EndScopes(facts, _localScopes, ref _localScopeCount);
        EndScopes(facts, _corridorScopes, ref _corridorScopeCount);
    }

    private static void EndScopes(
        PresentationWorldFactPublisher facts,
        (string Key, int Scope)[] scopes,
        ref int scopeCount)
    {
        if (!facts.Equals(default(PresentationWorldFactPublisher)))
        {
            for (int i = 0; i < scopeCount; i++)
            {
                (string key, int scope) = scopes[i];
                facts.PublishWorldSplineEnded(key, Entity.Null, scope);
            }
        }

        scopeCount = 0;
    }

    private Vector3 ResolveGroundOverlayCenter(int xCm, int zCm)
    {
        float pathOverlayY = _runtime.ActiveConfig.Presentation.PathOverlayY;
        IVisualHeightmap heightmap = _engine.GetService(CoreServiceKeys.VisualHeightmap)
            ?? throw new InvalidOperationException(
                "Dynamic NavBake placement preview requires VisualHeightmap so the footprint sits on the authored ground.");
        if (!heightmap.TrySampleHeightCm(xCm, zCm, out float heightCm))
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake placement preview is outside VisualHeightmap coverage at world cm ({xCm}, {zCm}).");
        }

        return new Vector3(
            SpatialScaleDefaults.CentimetersToMeters(xCm),
            (heightCm * 0.01f) + pathOverlayY,
            SpatialScaleDefaults.CentimetersToMeters(zCm));
    }

    private static Vector3 ToVisualMeters(int xCm, int zCm, float pathOverlayY)
    {
        return new Vector3(
            SpatialScaleDefaults.CentimetersToMeters(xCm),
            pathOverlayY,
            SpatialScaleDefaults.CentimetersToMeters(zCm));
    }
}
