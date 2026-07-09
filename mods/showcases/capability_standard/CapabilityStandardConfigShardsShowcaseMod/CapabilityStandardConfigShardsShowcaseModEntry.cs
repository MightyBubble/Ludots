using System.Numerics;
using Arch.Core;
using CapabilityStandardModExtensibleRuntimeShowcaseShared;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace CapabilityStandardConfigShardsShowcaseMod;

public sealed class CapabilityStandardConfigShardsShowcaseModEntry : IMod
{
    private const string MapId = "capability_standard_config_shards_showcase";
    private const string AbilityId = "Ability.CapabilityStandard.ConfigShards.EmberBolt";
    private const string EffectId = "Effect.CapabilityStandard.ConfigShards.EmberBoltDamage";
    private const string HealthAttribute = "Health";
    private const float StartingHealth = 100f;
    private Entity _sourceEntity = Entity.Null;
    private Entity _targetEntity = Entity.Null;
    private int _healthAttributeId;

    public void OnLoad(IModContext context)
    {
        var runtime = new ExtensibleRuntimeShowcaseRuntime(new ExtensibleRuntimeShowcaseScenario
        {
            MapId = MapId,
            PanelElementId = "capability-standard-config-shards-panel",
            PrimaryButtonElementId = "capability-standard-config-shards-cast",
            SurfaceOwnerId = "Showcase.CapabilityStandardConfigShards.Panel",
            Title = "Config Shards",
            FeatureLabel = "GAS shards",
            PrimaryButtonLabel = "Cast Ember Bolt",
            AccentColor = "#62D58A",
            ReadyText = "Ember Bolt is assembled from its own ability and effect files.",
            ProofLines =
            [
                "Ability shard: GAS/abilities/capability_standard.config_shards.ember_bolt.json",
                "Effect shard: GAS/effects/capability_standard.config_shards.ember_bolt_damage.json",
                "No Core ability file edit is required."
            ],
            OnActivated = VerifyLoadedShards,
            OnUpdate = UpdateMetrics,
            OnPrimaryAction = CastEmberBolt
        });

        ExtensibleRuntimeShowcaseBootstrap.Install(context, runtime, nameof(CapabilityStandardConfigShardsShowcaseMod));
    }

    public void OnUnload()
    {
    }

    private void VerifyLoadedShards(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        int abilityId = AbilityIdRegistry.GetId(AbilityId);
        int effectId = EffectTemplateIdRegistry.GetId(EffectId);
        var abilities = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
            ?? throw new InvalidOperationException("Config shards showcase requires AbilityDefinitionRegistry.");
        var effects = engine.GetService(CoreServiceKeys.EffectTemplateRegistry)
            ?? throw new InvalidOperationException("Config shards showcase requires EffectTemplateRegistry.");

        AbilityDefinition data = default;
        bool abilityLoaded = abilityId > 0 && abilities.TryGet(abilityId, out data);
        bool effectLoaded = effectId > 0 && effects.TryGet(effectId, out _);

        if (!abilityLoaded || !effectLoaded)
        {
            throw new InvalidOperationException(
                $"Config shards showcase requires '{AbilityId}' and '{EffectId}' in the formal GAS registries.");
        }

        _healthAttributeId = AttributeRegistry.Register(HealthAttribute);
        VerifyAbilityExecReferencesEffect(in data, effectId);
        EnsureCombatants(engine, abilityId);

        runtime.SetMetricA("Ability", abilityLoaded ? "loaded" : "missing");
        runtime.SetMetricB("Target HP", ReadTargetHealth(engine).ToString("0"));
        runtime.SetLastEvent("Ember Bolt is ready from its own ability and effect files.");
    }

    private void UpdateMetrics(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        EnsureCombatants(engine, AbilityIdRegistry.GetId(AbilityId));
        runtime.SetMetricA("Ability", "loaded");
        runtime.SetMetricB("Target HP", ReadTargetHealth(engine).ToString("0"));
        if (runtime.PrimaryActionCount > 0 && ReadTargetHealth(engine) < StartingHealth)
        {
            runtime.SetHighlightRight(true);
            runtime.SetLastEvent("Ember Bolt hit the target and reduced its health.");
        }
    }

    private void CastEmberBolt(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        int effectId = EffectTemplateIdRegistry.GetId(EffectId);
        if (effectId <= 0)
        {
            throw new InvalidOperationException($"Effect '{EffectId}' is not registered.");
        }

        int abilityId = AbilityIdRegistry.GetId(AbilityId);
        if (abilityId <= 0)
        {
            throw new InvalidOperationException($"Ability '{AbilityId}' is not registered.");
        }

        var abilities = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
            ?? throw new InvalidOperationException("Config shards showcase requires AbilityDefinitionRegistry.");
        if (!abilities.TryGet(abilityId, out _))
        {
            throw new InvalidOperationException($"Ability '{AbilityId}' is missing from AbilityDefinitionRegistry.");
        }

        EnsureCombatants(engine, abilityId);
        var orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("Config shards showcase requires OrderTypeRegistry.");
        var orderRules = engine.GetService(CoreServiceKeys.OrderRuleRegistry);
        int castOrderTypeId = orderTypes.GetId("castAbility");
        OrderSubmitResult submitResult = OrderSubmitter.Submit(
            engine.World,
            _sourceEntity,
            new Order
            {
                OrderId = runtime.PrimaryActionCount,
                OrderTypeId = castOrderTypeId,
                Actor = _sourceEntity,
                Target = _targetEntity,
                TargetContext = _targetEntity,
                Args = new OrderArgs
                {
                    I0 = 0
                },
                SubmitStep = engine.GameSession?.CurrentTick ?? 0,
                SubmitMode = OrderSubmitMode.Immediate
            },
            orderTypes,
            orderRules,
            engine.GameSession?.CurrentTick ?? 0,
            stepRateHz: 30);

        if (submitResult != OrderSubmitResult.Activated)
        {
            throw new InvalidOperationException($"Config shards showcase expected castAbility order to activate, got {submitResult}.");
        }

        runtime.SetHighlightRight(true);
        runtime.SetMetricA("Ability", "casting");
        runtime.SetMetricB("Target HP", ReadTargetHealth(engine).ToString("0"));
        runtime.SetLastEvent("Ember Bolt was cast from the split ability file.");
    }

    private void EnsureCombatants(GameEngine engine, int abilityId)
    {
        if (abilityId <= 0)
        {
            throw new InvalidOperationException($"Ability '{AbilityId}' is not registered.");
        }

        if (_healthAttributeId <= 0)
        {
            _healthAttributeId = AttributeRegistry.Register(HealthAttribute);
        }

        if (_sourceEntity == Entity.Null || !engine.World.IsAlive(_sourceEntity))
        {
            var abilitySlots = new AbilityStateBuffer();
            abilitySlots.AddAbility(abilityId);
            _sourceEntity = engine.World.Create(new VisualTransform
            {
                Position = new Vector3(5.2f, 0f, 7.4f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One
            },
            abilitySlots,
            OrderBuffer.CreateEmpty(),
            new BlackboardIntBuffer(),
            new BlackboardEntityBuffer());
        }
        else if (!engine.World.Has<AbilityStateBuffer>(_sourceEntity) ||
                 !engine.World.Has<OrderBuffer>(_sourceEntity) ||
                 !engine.World.Has<BlackboardIntBuffer>(_sourceEntity) ||
                 !engine.World.Has<BlackboardEntityBuffer>(_sourceEntity))
        {
            throw new InvalidOperationException("Config shards showcase source entity is missing formal ability/order buffers.");
        }

        if (_targetEntity != Entity.Null && engine.World.IsAlive(_targetEntity))
        {
            return;
        }

        var attributes = new AttributeBuffer();
        attributes.SetBase(_healthAttributeId, StartingHealth);
        attributes.SetCurrent(_healthAttributeId, StartingHealth);
        _targetEntity = engine.World.Create(
            new VisualTransform
            {
                Position = new Vector3(11.4f, 0f, 5.2f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One
            },
            attributes);
    }

    private float ReadTargetHealth(GameEngine engine)
    {
        if (_targetEntity == Entity.Null ||
            !engine.World.IsAlive(_targetEntity) ||
            !engine.World.Has<AttributeBuffer>(_targetEntity))
        {
            throw new InvalidOperationException("Config shards showcase target entity is missing its AttributeBuffer.");
        }

        return engine.World.Get<AttributeBuffer>(_targetEntity).GetCurrent(_healthAttributeId);
    }

    private static void VerifyAbilityExecReferencesEffect(in AbilityDefinition data, int effectId)
    {
        for (int i = 0; i < data.ExecSpec.ItemCount; i++)
        {
            if (data.ExecSpec.GetKind(i) == ExecItemKind.EffectSignal &&
                data.ExecSpec.GetTemplateId(i) == effectId)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Config shards showcase ability '{AbilityId}' must execute EffectSignal '{EffectId}'.");
    }
}
