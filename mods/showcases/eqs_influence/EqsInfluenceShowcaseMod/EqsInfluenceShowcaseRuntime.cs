using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Fields.Influence;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial.Eqs;
using Ludots.Core.Spatial.Eqs.Config;
using Ludots.Core.Systems;

namespace EqsInfluenceShowcaseMod;

internal sealed class EqsInfluenceShowcaseRuntime
{
    public const string ScenarioId = "avoid_threat_demo";
    public const string InstalledKey = "EqsInfluenceShowcase.Installed";
    public const string RuntimeKey = "EqsInfluenceShowcase.Runtime";

    private readonly EqsInfluenceConfigDocument _document;
    private readonly EqsScenarioConfig _scenario;
    private readonly EqsQuery _query;
    private InfluenceFieldRegistry? _registry;
    private EqsItem[] _candidates = Array.Empty<EqsItem>();
    private int _candidateCount;
    private EqsItem _best;
    private bool _hasBest;
    private bool _armed;

    public EqsInfluenceShowcaseRuntime(EqsInfluenceConfigDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _scenario = EqsInfluenceConfigLoader.RequireScenario(document, ScenarioId);
        EqsQueryConfig queryConfig = EqsInfluenceConfigLoader.RequireQuery(document, _scenario.QueryId);
        _query = EqsInfluenceConfigLoader.CreateQuery(queryConfig);
    }

    public void Arm(GameEngine engine)
    {
        if (_armed)
        {
            return;
        }

        _registry = EqsInfluenceConfigLoader.MaterializeFields(_document, _scenario.InfluenceFieldIds);
        _registry.PresentationNormalizePeak = _scenario.Presentation.NormalizePeak;
        engine.SetService(CoreServiceKeys.InfluenceFieldRegistry, _registry);

        if (engine.TryGetService(CoreServiceKeys.RenderDebugState, out RenderDebugState debug) && debug != null)
        {
            debug.DrawFieldOverlays = true;
        }

        RunQuery(engine.World);
        _armed = true;
    }

    public void TickPresentation(GameEngine engine)
    {
        if (!_armed || _registry == null)
        {
            return;
        }

        if (engine.TryGetService(CoreServiceKeys.RenderDebugState, out RenderDebugState debug) && debug != null)
        {
            debug.DrawFieldOverlays = true;
        }

        if (!engine.TryGetService(CoreServiceKeys.GroundOverlayBuffer, out GroundOverlayBuffer overlays) || overlays == null)
        {
            throw new InvalidOperationException("EqsInfluenceShowcase requires GroundOverlayBuffer.");
        }

        EmitOverlays(overlays);
    }

    public bool TryGetBest(out EqsItem best)
    {
        best = _best;
        return _hasBest;
    }

    public InfluenceFieldRegistry RequireRegistry()
        => _registry ?? throw new InvalidOperationException("Showcase runtime is not armed.");

    private void RunQuery(World world)
    {
        if (_registry == null)
        {
            throw new InvalidOperationException("Registry must be materialized before running EQS.");
        }

        var ctx = new EqsContext(_scenario.Origin, world, influenceFields: _registry);
        if (_candidates.Length < 64)
        {
            _candidates = new EqsItem[64];
        }

        _candidateCount = _query.Run(in ctx, _candidates);
        _hasBest = EqsSelection.Best(_candidates.AsSpan(0, _candidateCount), out _best);
        if (!_hasBest)
        {
            throw new InvalidOperationException("EQS scenario produced no selectable candidate.");
        }
    }

    private void EmitOverlays(GroundOverlayBuffer overlays)
    {
        EqsPresentationConfig presentation = _scenario.Presentation;
        float maxScore = 0f;
        for (int i = 0; i < _candidateCount; i++)
        {
            if (!_candidates[i].Filtered)
            {
                maxScore = Math.Max(maxScore, _candidates[i].Score);
            }
        }

        if (presentation.DrawCandidates)
        {
            for (int i = 0; i < _candidateCount; i++)
            {
                ref readonly EqsItem item = ref _candidates[i];
                if (item.Filtered)
                {
                    continue;
                }

                float t = maxScore <= 0f ? 0.5f : Math.Clamp(item.Score / maxScore, 0f, 1f);
                overlays.Upsert(new GroundOverlayItem
                {
                    StableId = 10_000 + i,
                    Shape = GroundOverlayShape.Circle,
                    Center = WorldPlane2D.LogicCmToVisualMeters(item.Position.X, item.Position.Y, 0.12f),
                    Radius = 0.7f,
                    FillColor = new Vector4(0.15f + (0.55f * t), 0.88f, 0.45f, 0.35f + (0.35f * t)),
                    BorderColor = new Vector4(0.1f, 0.95f, 0.55f, 0.95f),
                    BorderWidth = 0.08f
                });
            }
        }

        if (presentation.DrawBest && _hasBest)
        {
            overlays.Upsert(new GroundOverlayItem
            {
                StableId = 20_001,
                Shape = GroundOverlayShape.Ring,
                Center = WorldPlane2D.LogicCmToVisualMeters(_best.Position.X, _best.Position.Y, 0.16f),
                Radius = 1.25f,
                InnerRadius = 0.85f,
                FillColor = new Vector4(0.98f, 0.84f, 0.15f, 0.22f),
                BorderColor = new Vector4(1f, 0.86f, 0.1f, 1f),
                BorderWidth = 0.1f
            });
        }

        // Actor origin probe
        overlays.Upsert(new GroundOverlayItem
        {
            StableId = 20_000,
            Shape = GroundOverlayShape.Circle,
            Center = WorldPlane2D.LogicCmToVisualMeters(0, 0, 0.1f),
            Radius = 0.55f,
            FillColor = new Vector4(0.12f, 0.14f, 0.18f, 0.55f),
            BorderColor = new Vector4(0.95f, 0.95f, 0.95f, 0.95f),
            BorderWidth = 0.07f
        });

        // Goal marker
        overlays.Upsert(new GroundOverlayItem
        {
            StableId = 20_002,
            Shape = GroundOverlayShape.Circle,
            Center = WorldPlane2D.LogicCmToVisualMeters(500, 0, 0.1f),
            Radius = 0.65f,
            FillColor = new Vector4(0.2f, 0.5f, 1f, 0.4f),
            BorderColor = new Vector4(0.35f, 0.7f, 1f, 1f),
            BorderWidth = 0.08f
        });

        // Threat source ring (matches stamp radius 200cm)
        overlays.Upsert(new GroundOverlayItem
        {
            StableId = 20_003,
            Shape = GroundOverlayShape.Ring,
            Center = WorldPlane2D.LogicCmToVisualMeters(300, 0, 0.1f),
            Radius = 2.05f,
            InnerRadius = 1.85f,
            FillColor = new Vector4(0.95f, 0.25f, 0.18f, 0.08f),
            BorderColor = new Vector4(1f, 0.35f, 0.2f, 0.95f),
            BorderWidth = 0.08f
        });
    }
}

internal sealed class EqsInfluenceShowcaseSimulationSystem : BaseSystem<World, float>
{
    public EqsInfluenceShowcaseSimulationSystem(GameEngine engine, EqsInfluenceShowcaseRuntime runtime)
        : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
    {
        _ = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override void Update(in float deltaTime)
    {
        // Scenario is static after arm; reserved for future live re-query.
    }
}

internal sealed class EqsInfluenceShowcasePresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly EqsInfluenceShowcaseRuntime _runtime;

    public EqsInfluenceShowcasePresentationSystem(GameEngine engine, EqsInfluenceShowcaseRuntime runtime)
        : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
    {
        _engine = engine;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override void Update(in float deltaTime)
    {
        _runtime.TickPresentation(_engine);
    }
}
