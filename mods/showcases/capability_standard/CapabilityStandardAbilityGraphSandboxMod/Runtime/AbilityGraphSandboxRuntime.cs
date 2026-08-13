using System;
using System.Diagnostics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Spatial;

namespace CapabilityStandardAbilityGraphSandboxMod.Runtime;

public sealed class AbilityGraphSandboxRuntime : IDisposable
{
    private const int CasterTeamId = 1;
    private const int TargetTeamId = 2;
    private const uint CasterLayer = 0b0001;
    private const uint TargetLayer = 0b0010;

    private readonly GraphShowcaseConfig _config = new();
    private AbilityGraphSandboxBundle? _bundle;
    private GraphProgramRegistry? _programs;
    private World? _world;
    private GasGraphRuntimeApi? _api;
    private EffectRequestQueue? _requests;
    private RelationshipRuntime? _relationships;
    private TagOps? _tagOps;
    private SpatialCoordinateConverter? _coords;
    private GridSpatialPartitionWorld? _grid;
    private Entity _caster;
    private Entity[] _targets = Array.Empty<Entity>();
    private int _nearbyCountKeyId;
    private int _nearestKeyId;
    private int _statusTokenKeyId;
    private int _buffTemplateKeyId;
    private int _loyaltyKeyId;
    private float _accum;
    private int _castWave;
    private float[] _tx = Array.Empty<float>();
    private float[] _ty = Array.Empty<float>();
    private byte[] _flash = Array.Empty<byte>();
    private int _lastHit = -1;
    private int _nearbyCount;
    private int _effectApplications;
    private int _relationshipScore;
    private bool _trustedFlag;
    private string _statusToken = "无";

    public float CasterX => 0f;
    public float CasterY => 0f;
    public float[] TargetX => _tx;
    public float[] TargetY => _ty;
    public byte[] Flash => _flash;
    public int TargetCount => _tx.Length;
    public int LastHit => _lastHit;
    public int NearbyCount => _nearbyCount;
    public int EffectApplications => _effectApplications;
    public int RelationshipScore => _relationshipScore;
    public bool TrustedFlag => _trustedFlag;
    public string StatusToken => _statusToken;
    public GraphProgramRegistry? Programs => _programs;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_ability_graph_sandbox" };

    public void BindStandaloneFromModAssets()
    {
        _bundle = AbilityGraphSandboxGraphBootstrap.LoadModGraphs(
            AbilityGraphSandboxGraphBootstrap.FindModAssetsRoot());
        _programs = _bundle.Programs;
    }

    public void EnsureWorld()
    {
        if (_world != null) return;
        if (_programs == null || _bundle == null)
        {
            throw new InvalidOperationException(
                "AbilityGraphSandboxRuntime.BindStandaloneFromModAssets() required before EnsureWorld.");
        }

        RequireGraph(AbilityGraphSandboxGraphKeys.Scout);
        RequireGraph(AbilityGraphSandboxGraphKeys.Apply);
        RequireGraph(AbilityGraphSandboxGraphKeys.Bond);

        _nearbyCountKeyId = ConfigKeyRegistry.Register(AbilityGraphSandboxGraphKeys.NearbyCountKey);
        _nearestKeyId = ConfigKeyRegistry.Register(AbilityGraphSandboxGraphKeys.NearestKey);
        _statusTokenKeyId = ConfigKeyRegistry.Register(AbilityGraphSandboxGraphKeys.StatusTokenKey);
        _buffTemplateKeyId = ConfigKeyRegistry.Register(AbilityGraphSandboxGraphKeys.BuffTemplateKey);
        _loyaltyKeyId = ConfigKeyRegistry.Register(AbilityGraphSandboxGraphKeys.LoyaltyKey);

        _world = World.Create();
        _requests = new EffectRequestQueue();
        _coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
        _grid = new GridSpatialPartitionWorld(cellSize: 4);
        var spatial = new SpatialQueryService(new GridSpatialPartitionBackend(_grid, _coords));
        spatial.SetCoordinateConverter(_coords);
        spatial.SetPositionProvider(entity =>
        {
            ref WorldPositionCm pos = ref _world!.Get<WorldPositionCm>(entity);
            return pos.Value.ToWorldCmInt2();
        });

        _tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
        _relationships = new RelationshipRuntime(
            _world,
            _bundle.Types,
            _bundle.Metrics,
            _bundle.Flags,
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(_world));
        var entityQueries = new EntitySetQueryRuntime(_world, _tagOps, _relationships);
        _api = new GasGraphRuntimeApi(
            _world,
            spatial,
            _coords,
            eventBus: null,
            effectRequests: _requests,
            tagOps: _tagOps,
            relationshipRuntime: _relationships,
            typeRegistry: _bundle.Types,
            metricRegistry: _bundle.Metrics,
            flagRegistry: _bundle.Flags,
            reasonRegistry: _bundle.Reasons,
            entityQueries: entityQueries,
            tagDisplayTables: _bundle.TagDisplay);

        _caster = SpawnCombatant(0, 0, CasterTeamId, CasterLayer, inspired: false);
        ref BlackboardIntBuffer casterBb = ref _world.Get<BlackboardIntBuffer>(_caster);
        casterBb.Set(_buffTemplateKeyId, _bundle.BuffTemplateId);

        int targets = Math.Min(_config.FeaturedAgentCount, 8);
        _tx = new float[targets];
        _ty = new float[targets];
        _flash = new byte[targets];
        _targets = new Entity[targets];
        for (int i = 0; i < targets; i++)
        {
            float t = targets <= 1 ? 0.5f : i / (float)(targets - 1);
            float ang = -0.7f + t * 1.4f;
            int xCm = (int)MathF.Round(MathF.Sin(ang) * 600f);
            int yCm = (int)MathF.Round((4.5f + MathF.Cos(ang) * 0.8f) * 100f);
            bool inspired = (i & 1) == 0;
            _targets[i] = SpawnCombatant(xCm, yCm, TargetTeamId, TargetLayer, inspired);
            _tx[i] = xCm * 0.01f;
            _ty[i] = yCm * 0.01f;
        }

        Metrics.AgentCount = targets;
        Metrics.Detail = "巡逻队就位：查一圈找目标，命中后挂状态、加好感，并读状态牌。";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        for (int i = 0; i < _flash.Length; i++)
        {
            if (_flash[i] > 0) _flash[i]--;
        }

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;
        _castWave++;

        var sw = Stopwatch.StartNew();
        WorldCmInt2 origin = _world!.Get<WorldPositionCm>(_caster).ToWorldCmInt2();
        var originCm = new IntVector2(origin.X, origin.Y);

        ExecuteEffectGraph(AbilityGraphSandboxGraphKeys.Scout, Entity.Null, originCm);
        ReadScoutResults();

        _requests!.Clear();
        ExecuteEffectGraph(AbilityGraphSandboxGraphKeys.Apply, _targets[Math.Max(_lastHit, 0)], originCm);
        _effectApplications += _requests.Count;
        FlashEffectTargets();

        Entity bondTarget = _lastHit >= 0 ? _targets[_lastHit] : _targets[0];
        ExecuteEffectGraph(AbilityGraphSandboxGraphKeys.Bond, bondTarget, originCm);
        ReadBondResults(bondTarget);
        sw.Stop();

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            $"巡逻查一圈：{_nearbyCount}个目标；对{_lastHit + 1}号挂状态「{_statusToken}」；" +
            $"加好感+3={_relationshipScore}；状态牌读到「{_statusToken}」(token={StatusTokenId})；" +
            $"信任旗{(_trustedFlag ? "已点亮" : "未点亮")}；本波效果申请{_requests.Count}条；耗时{Metrics.LastThinkMs:F3}ms";
    }

    public void Dispose()
    {
        _world?.Dispose();
        _world = null;
    }

    private int StatusTokenId
    {
        get
        {
            ref BlackboardIntBuffer ints = ref _world!.Get<BlackboardIntBuffer>(_caster);
            return ints.TryGet(_statusTokenKeyId, out int token) ? token : 0;
        }
    }

    private void ExecuteEffectGraph(string graphKey, Entity explicitTarget, IntVector2 originCm)
    {
        int graphId = GraphIdRegistry.GetId(graphKey);
        if (!_programs!.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
        {
            throw new InvalidOperationException($"Graph '{graphKey}' is not registered.");
        }

        GraphExecutor.Execute(_world!, _caster, explicitTarget, originCm, program, _api!, GraphKind.Effect);
    }

    private void ReadScoutResults()
    {
        ref BlackboardIntBuffer ints = ref _world!.Get<BlackboardIntBuffer>(_caster);
        ref BlackboardEntityBuffer entities = ref _world.Get<BlackboardEntityBuffer>(_caster);
        if (!ints.TryGet(_nearbyCountKeyId, out _nearbyCount))
        {
            throw new InvalidOperationException("Scout graph did not write nearbyCount.");
        }

        if (!entities.TryGet(_nearestKeyId, out Entity nearest) || nearest == Entity.Null)
        {
            throw new InvalidOperationException("Scout graph did not write nearest target.");
        }

        if (!ints.TryGet(_statusTokenKeyId, out int token) || token <= 0)
        {
            throw new InvalidOperationException("Scout graph did not write LookupTagDisplayToken.");
        }

        _statusToken = TokenDisplayName(token);
        _lastHit = IndexOf(nearest);
        if (_lastHit >= 0)
        {
            _flash[_lastHit] = 12;
        }
    }

    private void ReadBondResults(Entity bondTarget)
    {
        ref BlackboardIntBuffer ints = ref _world!.Get<BlackboardIntBuffer>(_caster);
        if (!ints.TryGet(_loyaltyKeyId, out _relationshipScore))
        {
            throw new InvalidOperationException("Bond graph did not write loyalty metric.");
        }

        _trustedFlag = _relationships!.HasFlag(_caster, bondTarget, _bundle!.SocialBondTypeId, _bundle.TrustedFlagId);
        if (_relationshipScore <= 0)
        {
            throw new InvalidOperationException("Bond graph left loyalty at a non-positive host metric.");
        }

        if (!_trustedFlag)
        {
            throw new InvalidOperationException("Bond graph RelationshipHasFlag expected Trusted after SetFlag.");
        }
    }

    private void FlashEffectTargets()
    {
        for (int i = 0; i < _requests!.Count; i++)
        {
            int index = IndexOf(_requests[i].Target);
            if (index >= 0)
            {
                _flash[index] = 12;
            }
        }
    }

    private int IndexOf(Entity entity)
    {
        for (int i = 0; i < _targets.Length; i++)
        {
            if (_targets[i] == entity)
            {
                return i;
            }
        }

        return -1;
    }

    private Entity SpawnCombatant(int xCm, int yCm, int teamId, uint layerCategory, bool inspired)
    {
        Entity entity = _world!.Create(
            new MapEntity(),
            new Team { Id = teamId },
            WorldPositionCm.FromCm(xCm, yCm),
            new EntityLayer(category: layerCategory, mask: uint.MaxValue),
            new BlackboardIntBuffer(),
            new BlackboardEntityBuffer(),
            new GameplayTagContainer(),
            new TagCountContainer(),
            new DirtyFlags());
        AddToGrid(entity, xCm, yCm);
        int tagId = inspired ? _bundle!.InspiredTagId : _bundle!.MarkedTagId;
        if (!_tagOps!.AddTag(_world, entity, tagId))
        {
            throw new InvalidOperationException($"Failed to seed status tag {tagId} on sandbox combatant.");
        }

        return entity;
    }

    private void AddToGrid(Entity entity, int xCm, int yCm)
    {
        IntVector2 grid = _coords!.WorldToGrid(new WorldCmInt2(xCm, yCm));
        _grid!.Add(entity, new IntRect(grid.X, grid.Y, grid.X + 1, grid.Y + 1));
    }

    private void RequireGraph(string graphKey)
    {
        int graphId = GraphIdRegistry.GetId(graphKey);
        if (graphId <= 0 || !_programs!.TryGetProgram(graphId, out _))
        {
            throw new InvalidOperationException($"Required graph '{graphKey}' is missing from registry.");
        }
    }

    private static string TokenDisplayName(int tokenId)
        => tokenId switch
        {
            AbilityGraphSandboxGraphKeys.InspiredTokenId => "鼓舞",
            AbilityGraphSandboxGraphKeys.MarkedTokenId => "标记",
            _ => throw new InvalidOperationException($"LookupTagDisplayToken returned unmapped token {tokenId}.")
        };
}
