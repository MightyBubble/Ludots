using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Scripting;

namespace CapabilityStandardAbilityFeatureGalleryMod.Runtime;

public sealed class AbilityFeatureGalleryRuntime : IDisposable
{
    public const float FrameStep = 1f / 60f;

    private string? _feature;
    private string? _assetsRoot;
    private AbilityFeatureVignette? _vignette;
    private GameEngine? _engine;
    private bool _ownsEngine;
    private bool _graphRanHandlerBound;
    private int _scriptIndex;
    private int _orderSerial;
    private bool _outcomeObserverBound;
    private string _castSlotThisFrame = "";
    private readonly Dictionary<string, Entity> _actors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenEvents = new(StringComparer.Ordinal);

    public AbilityFeatureMetrics Metrics { get; } = new();
    public bool IsBound => _feature != null;
    public string Title => _vignette?.Title ?? "";
    public string Feature => _feature ?? "";
    public AbilityFeatureVignette Vignette =>
        _vignette ?? throw new InvalidOperationException("BindFeature required before reading vignette.");

    public void AttachEngine(GameEngine engine, bool ownsEngine = false)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ownsEngine = ownsEngine;
        BindGraphRanHandler();
        BindOutcomeObserver();
    }

    public void BindFromStartupMapId(string? mapId)
    {
        if (!AbilityFeatureIds.TryParseFeatureFromMapId(mapId, out string feature))
        {
            throw new InvalidOperationException(
                $"Ability feature gallery requires startupMapId '{AbilityFeatureIds.ShowcaseIdPrefix}{{Feature}}', got '{mapId}'.");
        }

        BindFeature(feature);
    }

    public void BindFeature(string feature)
    {
        if (_feature != null)
        {
            if (!string.Equals(_feature, AbilityFeatureIds.RequireFeatureName(feature), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Ability feature gallery already bound to '{_feature}', cannot bind '{feature}'.");
            }

            return;
        }

        _feature = AbilityFeatureIds.RequireFeatureName(feature);
        _assetsRoot = ResolveAssetsRoot();
        _vignette = AbilityFeatureVignette.Load(_assetsRoot, _feature);
        Metrics.ShowcaseId = AbilityFeatureIds.ShowcaseId(_feature);
        Metrics.Detail = _vignette.Beat;
    }

    public void EnsureActors()
    {
        if (_actors.Count > 0)
        {
            return;
        }

        GameEngine engine = RequireEngine();
        BindActor("caster", "施法者");
        BindActor("target", "木桩");
        if (_vignette!.ExtraActors.Contains("target2", StringComparer.Ordinal))
        {
            BindActor("target2", "远木桩");
        }

        if (_vignette.ExtraActors.Contains("wounded", StringComparer.Ordinal))
        {
            BindActor("wounded", "残血木桩");
        }

        if (_vignette.NeedsProgression)
        {
            BindActor("unlockBoard", "解锁牌");
        }

        Metrics.CasterBefore = ReadHealth(engine, Actor("caster"));
        Metrics.CasterAfter = Metrics.CasterBefore;
        Metrics.TargetBefore = ReadHealth(engine, Actor("target"));
        Metrics.TargetAfter = Metrics.TargetBefore;
        if (_actors.ContainsKey("target2"))
        {
            Metrics.Target2Before = ReadHealth(engine, Actor("target2"));
            Metrics.Target2After = Metrics.Target2Before;
        }

        if (_actors.ContainsKey("wounded"))
        {
            Metrics.WoundedBefore = ReadHealth(engine, Actor("wounded"));
            Metrics.WoundedAfter = Metrics.WoundedBefore;
        }
    }

    public void Tick(float dt)
    {
        if (_vignette == null || _engine == null)
        {
            return;
        }

        EnsureActors();
        ObserveEvents();
        ObserveExec();
        RefreshHealth();
        while (_scriptIndex < _vignette.Script.Length && Metrics.Frame >= _vignette.Script[_scriptIndex].AtFrame)
        {
            RunStep(_vignette.Script[_scriptIndex]);
            _scriptIndex++;
        }

        RefreshHealth();
        Metrics.Detail = RenderDetail();
        Metrics.Frame++;
    }

    public void PlayUntilSettled(int maxFrames = 90)
    {
        GameEngine engine = RequireEngine();
        EnsureActors();
        for (int i = 0; i < maxFrames; i++)
        {
            engine.Tick(Time.FixedDeltaTime);
            if (_scriptIndex >= _vignette!.Script.Length)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Ability feature '{_feature}' did not settle in {maxFrames} simulation frames; scriptIndex={_scriptIndex}/{_vignette!.Script.Length}; frame={Metrics.Frame}.");
    }

    public static string ResolveAssetsRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            string candidate = Path.Combine(dir, AbilityFeatureIds.ModAssetsRelative);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir) ?? "";
        }

        throw new InvalidOperationException("Ability feature gallery assets root was not found.");
    }

    private void BindGraphRanHandler()
    {
        if (_graphRanHandlerBound || _engine == null)
        {
            return;
        }

        _engine.TriggerManager.RegisterEventHandler(
            new EventKey("Event.AbilityFeature.GraphRan"),
            _ =>
            {
                Metrics.TriggerGraphFired = true;
                return Task.CompletedTask;
            });
        _graphRanHandlerBound = true;
    }

    private void BindOutcomeObserver()
    {
        if (_outcomeObserverBound || _engine == null)
        {
            return;
        }

        _engine.RegisterSystem(
            new AbilityFeatureGalleryCastOutcomeSystem(_engine, this),
            SystemGroup.Cleanup);
        _outcomeObserverBound = true;
    }

    public void ObserveCastOutcomes()
    {
        if (string.IsNullOrEmpty(_castSlotThisFrame) || _engine == null)
        {
            return;
        }

        GasPresentationEventBuffer? buffer = _engine.GetService(CoreServiceKeys.GasPresentationEventBuffer);
        if (buffer == null)
        {
            throw new InvalidOperationException("Ability feature gallery requires GasPresentationEventBuffer.");
        }

        Entity caster = Actor("caster");
        ReadOnlySpan<GasPresentationEvent> events = buffer.Events;
        for (int i = 0; i < events.Length; i++)
        {
            if (events[i].Kind == GasPresentationEventKind.CastFailed && events[i].Actor == caster)
            {
                if (string.Equals(_castSlotThisFrame, "second", StringComparison.Ordinal))
                {
                    Metrics.SecondCast = "rejected";
                }
                else
                {
                    Metrics.FirstCast = "rejected";
                }

                break;
            }
        }

        _castSlotThisFrame = "";
        Metrics.Detail = RenderDetail();
    }

    public void Dispose()
    {
        _actors.Clear();
        if (_ownsEngine)
        {
            _engine?.Dispose();
        }

        _engine = null;
    }

    public bool ActorHasTag(string role, string tag) => HasTag(ResolveActor(role), tag);

    private void RunStep(AbilityFeatureScriptStep step)
    {
        GameEngine engine = RequireEngine();
        switch (step.Op)
        {
            case "cast":
                RecordCast(step.SaveAs, SubmitCast(step.Slot, ResolveActor(step.Target ?? "target")));
                break;
            case "confirm":
                ConfirmGate(collection: false, targets: []);
                break;
            case "confirmCollection":
                ConfirmGate(collection: true, targets: step.Targets);
                break;
            case "publishEvent":
                PublishEvent(step.Tag ?? throw new InvalidOperationException("publishEvent requires tag."));
                break;
            case "addTag":
                AddTag(ResolveActor(step.Entity ?? "caster"), step.Tag ?? throw new InvalidOperationException("addTag requires tag."));
                break;
            case "unlockProgression":
                UnlockProgression();
                break;
            case "snapshotVisible":
                int visible = CountVisibleAbilities();
                if (string.Equals(step.SaveAs, "after", StringComparison.Ordinal))
                {
                    Metrics.VisibleAfterCount = visible;
                }
                else
                {
                    Metrics.VisibleBeforeCount = visible;
                }

                break;
            case "snapshotSlot":
                string abilityName = ReadSlotAbilityName(step.Slot);
                if (string.Equals(step.SaveAs, "after", StringComparison.Ordinal))
                {
                    Metrics.Slot0After = abilityName;
                }

                break;
            case "assert":
                if (!string.IsNullOrWhiteSpace(step.CasterHasTag) && !HasTag(Actor("caster"), step.CasterHasTag))
                {
                    throw new InvalidOperationException($"Feature '{_feature}' expected caster tag '{step.CasterHasTag}'.");
                }

                if (!string.IsNullOrWhiteSpace(step.TargetHasTag) && !HasTag(Actor("target"), step.TargetHasTag))
                {
                    throw new InvalidOperationException($"Feature '{_feature}' expected target tag '{step.TargetHasTag}'.");
                }

                break;
            case "settle":
                break;
            default:
                throw new InvalidOperationException($"Unknown ability feature script op '{step.Op}'.");
        }

        _ = engine;
    }

    private string SubmitCast(int slot, Entity target)
    {
        GameEngine engine = RequireEngine();
        Entity caster = Actor("caster");
        var orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("Ability feature gallery requires OrderTypeRegistry.");
        var orderRules = engine.GetService(CoreServiceKeys.OrderRuleRegistry);
        int castOrderTypeId = orderTypes.GetId("castAbility");
        _orderSerial++;
        OrderSubmitResult result = OrderSubmitter.Submit(
            engine.World,
            caster,
            new Order
            {
                OrderId = _orderSerial,
                OrderTypeId = castOrderTypeId,
                Actor = caster,
                Target = target,
                TargetContext = target,
                Args = new OrderArgs { I0 = slot },
                SubmitStep = engine.GameSession?.CurrentTick ?? 0,
                SubmitMode = OrderSubmitMode.Immediate
            },
            orderTypes,
            orderRules,
            engine.GameSession?.CurrentTick ?? 0,
            stepRateHz: 30);

        return result == OrderSubmitResult.Activated ? "submitted" : "rejected";
    }

    private void ConfirmGate(bool collection, string[] targets)
    {
        GameEngine engine = RequireEngine();
        Entity caster = Actor("caster");
        if (!engine.World.Has<AbilityExecInstance>(caster))
        {
            throw new InvalidOperationException($"Feature '{_feature}' confirm ran without a waiting exec.");
        }

        AbilityExecInstance exec = engine.World.Get<AbilityExecInstance>(caster);
        if (exec.State != AbilityExecRunState.GateWaiting)
        {
            throw new InvalidOperationException($"Feature '{_feature}' confirm expected GateWaiting, got {exec.State}.");
        }

        Metrics.WaitedForGate = true;
        InputResponseBuffer responses = engine.GetService(CoreServiceKeys.InputResponseBuffer)
            ?? throw new InvalidOperationException("Ability feature gallery requires InputResponseBuffer.");
        Entity first = collection && targets.Length > 0 ? ResolveActor(targets[0]) : Actor("target");
        if (!responses.TryAdd(new InputResponse
            {
                RequestId = exec.WaitRequestId,
                ResponseTagId = exec.WaitTagId,
                Source = caster,
                Target = first,
                TargetContext = first
            }))
        {
            throw new InvalidOperationException($"Feature '{_feature}' could not enqueue the gate response.");
        }
    }

    private void PublishEvent(string tag)
    {
        GameEngine engine = RequireEngine();
        int tagId = TagRegistry.Register(tag);
        engine.EventBus.Publish(new GameplayEvent
        {
            TagId = tagId,
            Source = Actor("caster"),
            Target = Actor("target")
        });
        _seenEvents.Add(tag);
        Metrics.EventCount++;
    }

    private void AddTag(Entity entity, string tag)
    {
        GameEngine engine = RequireEngine();
        TagOps tagOps = engine.GetService(CoreServiceKeys.TagOps)
            ?? throw new InvalidOperationException("Ability feature gallery requires TagOps.");
        int tagId = TagRegistry.Register(tag);
        if (!tagOps.AddTag(engine.World, entity, tagId))
        {
            throw new InvalidOperationException($"Feature '{_feature}' failed to add tag '{tag}'.");
        }
    }

    private void UnlockProgression()
    {
        GameEngine engine = RequireEngine();
        ProgressionRequirementEvaluator evaluator = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator)
            ?? throw new InvalidOperationException("Ability feature gallery requires ProgressionRequirementEvaluator.");
        int progressionId = ProgressionIdRegistry.GetId("Progression.AbilityFeature.Unlock");
        if (progressionId <= 0 || !evaluator.TryComplete(Actor("unlockBoard"), progressionId))
        {
            throw new InvalidOperationException("Feature UseRequirement/ShowRequirement could not light the unlock board.");
        }
    }

    private int CountVisibleAbilities()
    {
        GameEngine engine = RequireEngine();
        Entity caster = Actor("caster");
        if (!engine.World.Has<AbilityStateBuffer>(caster))
        {
            throw new InvalidOperationException("Caster is missing AbilityStateBuffer.");
        }

        AbilityDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
            ?? throw new InvalidOperationException("Ability feature gallery requires AbilityDefinitionRegistry.");
        ProgressionRequirementEvaluator? evaluator = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator);
        AbilityStateBuffer slots = engine.World.Get<AbilityStateBuffer>(caster);
        int visible = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (!AbilitySlotResolver.TryResolve(engine.World, caster, i, out AbilitySlotState slot) || slot.AbilityId <= 0)
            {
                continue;
            }

            if (!definitions.TryGet(slot.AbilityId, out AbilityDefinition def))
            {
                continue;
            }

            if (!def.HasShowProgressionRequirement)
            {
                visible++;
                continue;
            }

            var context = new RoleResolverContext(
                source: caster,
                actor: caster,
                subject: caster,
                explicitScopeHost: Actor("unlockBoard"));
            if (evaluator == null || !evaluator.Evaluate(def.ShowProgressionRequirementId, in context))
            {
                continue;
            }

            visible++;
        }

        return visible;
    }

    private string ReadSlotAbilityName(int slot)
    {
        GameEngine engine = RequireEngine();
        if (!AbilitySlotResolver.TryResolve(engine.World, Actor("caster"), slot, out AbilitySlotState state) || state.AbilityId <= 0)
        {
            return "";
        }

        return AbilityIdRegistry.GetName(state.AbilityId) ?? "";
    }

    private void RecordCast(string? saveAs, string result)
    {
        string slot = ResolveCastSlot(saveAs);
        _castSlotThisFrame = slot;
        if (string.Equals(slot, "second", StringComparison.Ordinal))
        {
            Metrics.SecondCast = result;
            return;
        }

        Metrics.FirstCast = result;
    }

    private string ResolveCastSlot(string? saveAs)
    {
        if (string.Equals(saveAs, "second", StringComparison.Ordinal))
        {
            return "second";
        }

        if (string.Equals(saveAs, "first", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(Metrics.FirstCast))
        {
            return "first";
        }

        return "second";
    }

    private void ObserveEvents()
    {
        if (_vignette == null)
        {
            return;
        }

        string? watch = _vignette.Expect.EventTag ?? "Event.AbilityFeature.Bell";
        int tagId = TagRegistry.GetId(watch);
        if (tagId <= 0)
        {
            return;
        }

        GameplayEventBus.EventList events = RequireEngine().EventBus.Events;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].TagId == tagId)
            {
                Metrics.EventCount++;
            }
        }
    }

    private void ObserveExec()
    {
        GameEngine engine = RequireEngine();
        Entity caster = Actor("caster");
        if (!engine.World.Has<AbilityExecInstance>(caster))
        {
            return;
        }

        AbilityExecInstance exec = engine.World.Get<AbilityExecInstance>(caster);
        if (exec.State == AbilityExecRunState.GateWaiting)
        {
            Metrics.WaitedForGate = true;
        }

        if (exec.State == AbilityExecRunState.Interrupted)
        {
            Metrics.Interrupted = true;
        }
    }

    private void RefreshHealth()
    {
        GameEngine engine = RequireEngine();
        Metrics.CasterAfter = ReadHealth(engine, Actor("caster"));
        Metrics.TargetAfter = ReadHealth(engine, Actor("target"));
        if (_actors.ContainsKey("target2"))
        {
            Metrics.Target2After = ReadHealth(engine, Actor("target2"));
        }

        if (_actors.ContainsKey("wounded"))
        {
            Metrics.WoundedAfter = ReadHealth(engine, Actor("wounded"));
        }
    }

    private string RenderDetail()
    {
        AbilityFeatureVignette vignette = _vignette!;
        string detail = vignette.DetailTemplate
            .Replace("{targetBefore}", Format(Metrics.TargetBefore))
            .Replace("{targetAfter}", Format(Metrics.TargetAfter))
            .Replace("{target2After}", Format(Metrics.Target2After))
            .Replace("{casterBefore}", Format(Metrics.CasterBefore))
            .Replace("{casterAfter}", Format(Metrics.CasterAfter))
            .Replace("{woundedAfter}", Format(Metrics.WoundedAfter))
            .Replace("{eventCount}", Metrics.EventCount.ToString())
            .Replace("{casterTagState}", DescribeTag(Actor("caster")))
            .Replace("{targetTagState}", DescribeTag(Actor("target")))
            .Replace("{secondCast}", string.IsNullOrWhiteSpace(Metrics.SecondCast) ? "还没出手" : (Metrics.SecondCast == "rejected" ? "放不出" : "打出去了"))
            .Replace("{visibleAbilities}", Metrics.VisibleAfterCount > 0 ? $"{Metrics.VisibleAfterCount} 个" : $"{Metrics.VisibleBeforeCount} 个")
            .Replace("{slot0Name}", string.IsNullOrWhiteSpace(Metrics.Slot0After) ? "还没换" : SlotLabel(Metrics.Slot0After))
            .Replace("{graphState}", Metrics.TriggerGraphFired ? "跑了" : "还没跑");
        return detail;
    }

    private static string SlotLabel(string abilityId)
    {
        return abilityId.Contains("Hammer", StringComparison.Ordinal) ? "锤砸" : abilityId;
    }

    private string DescribeTag(Entity entity)
    {
        if (HasTag(entity, "Mark.AbilityFeature.SelfTimed") || HasTag(entity, "Mark.AbilityFeature.TargetTimed"))
        {
            return "挂着一阵印";
        }

        if (HasTag(entity, "Mark.AbilityFeature.SelfInstant") || HasTag(entity, "Mark.AbilityFeature.TargetInstant"))
        {
            return "打上了印";
        }

        if (HasTag(entity, "State.AbilityFeature.ToggleOn"))
        {
            return "开着";
        }

        if (HasTag(entity, "Cooldown.AbilityFeature.Lock"))
        {
            return "挂着禁招印";
        }

        return "没有那枚印";
    }

    private bool HasTag(Entity entity, string tag)
    {
        GameEngine engine = RequireEngine();
        int tagId = TagRegistry.GetId(tag);
        if (tagId <= 0 || !engine.World.Has<GameplayTagContainer>(entity))
        {
            return false;
        }

        return engine.World.Get<GameplayTagContainer>(entity).HasTag(tagId);
    }

    private void BindActor(string role, string name)
    {
        Entity found = FindNamed(RequireEngine().World, name);
        if (found == Entity.Null)
        {
            throw new InvalidOperationException($"Ability feature '{_feature}' is missing actor '{name}'.");
        }

        _actors[role] = found;
    }

    private Entity Actor(string role) => _actors[role];

    private Entity ResolveActor(string roleOrName)
    {
        if (_actors.TryGetValue(roleOrName, out Entity entity))
        {
            return entity;
        }

        return roleOrName switch
        {
            "施法者" => Actor("caster"),
            "木桩" => Actor("target"),
            "远木桩" => Actor("target2"),
            "残血木桩" => Actor("wounded"),
            "解锁牌" => Actor("unlockBoard"),
            _ => throw new InvalidOperationException($"Unknown actor '{roleOrName}' in feature '{_feature}'.")
        };
    }

    private GameEngine RequireEngine()
    {
        return _engine ?? throw new InvalidOperationException("AttachEngine required.");
    }

    private static float ReadHealth(GameEngine engine, Entity entity)
    {
        int healthId = AttributeRegistry.GetId("Health");
        if (healthId < 0 || !engine.World.Has<AttributeBuffer>(entity))
        {
            throw new InvalidOperationException("Ability feature actor is missing Health.");
        }

        return engine.World.Get<AttributeBuffer>(entity).GetCurrent(healthId);
    }

    private static Entity FindNamed(World world, string entityName)
    {
        Entity result = Entity.Null;
        var named = new QueryDescription().WithAll<Name>();
        world.Query(in named, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.Ordinal))
            {
                result = entity;
            }
        });
        return result;
    }

    private static string Format(float value) => value.ToString("0");
}
