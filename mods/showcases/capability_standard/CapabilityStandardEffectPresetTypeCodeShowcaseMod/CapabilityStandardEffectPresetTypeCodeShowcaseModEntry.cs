using System.Numerics;
using Arch.Core;
using CapabilityStandardModExtensibleRuntimeShowcaseShared;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace CapabilityStandardEffectPresetTypeCodeShowcaseMod;

public sealed class CapabilityStandardEffectPresetTypeCodeShowcaseModEntry : IMod
{
    private const string MapId = "capability_standard_effect_preset_type_code_showcase";
    private const string HandlerKey = "CapabilityStandardEffectPresetTypeCodeShowcaseMod.ApplyHeatMark";
    private const string EffectId = "Effect.CapabilityStandard.EffectPresetTypeCode.HeatMark";
    private int _registeredHandlerId;
    private int _handlerCallCount;
    private Entity _targetEntity = Entity.Null;

    public void OnLoad(IModContext context)
    {
        _registeredHandlerId = context.Extensions.Gas.RegisterBuiltinHandler(
            HandlerKey,
            ApplyHeatMark,
            new EffectOperationMetadata(EffectOperationKind.Pure, EffectAtomicDomain.None, "ApplyHeatMark"));
        var runtime = new ExtensibleRuntimeShowcaseRuntime(new ExtensibleRuntimeShowcaseScenario
        {
            MapId = MapId,
            PanelElementId = "capability-standard-effect-preset-type-code-panel",
            PrimaryButtonElementId = "capability-standard-effect-preset-type-code-apply",
            SurfaceOwnerId = "Showcase.CapabilityStandardEffectPresetTypeCode.Panel",
            Title = "Effect Preset Type Code",
            FeatureLabel = "C# handler",
            PrimaryButtonLabel = "Apply Heat Mark",
            AccentColor = "#FF8A4C",
            ReadyText = "Heat Mark is declared by data and finished by Mod code.",
            ProofLines =
            [
                $"Handler key: {HandlerKey}",
                "Preset type shard: GAS/preset_types/capability_standard.effect_preset_type_code.heat_mark.json",
                "Effect shard: GAS/effects/capability_standard.effect_preset_type_code.heat_mark.json"
            ],
            OnActivated = VerifyPresetType,
            OnUpdate = UpdateMetrics,
            OnPrimaryAction = RequestHeatMark
        });

        ExtensibleRuntimeShowcaseBootstrap.Install(context, runtime, nameof(CapabilityStandardEffectPresetTypeCodeShowcaseMod));
    }

    public void OnUnload()
    {
    }

    private void VerifyPresetType(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        _handlerCallCount = 0;
        _targetEntity = CreateTargetEntity(engine);
        int effectId = EffectTemplateIdRegistry.GetId(EffectId);
        var effects = engine.GetService(CoreServiceKeys.EffectTemplateRegistry)
            ?? throw new InvalidOperationException("Effect preset type code showcase requires EffectTemplateRegistry.");
        var data = default(EffectTemplateData);
        bool effectLoaded = effectId > 0 && effects.TryGet(effectId, out data);
        bool dynamicPreset = effectLoaded && data.PresetType == EffectPresetType.None && data.PresetTypeId > byte.MaxValue;
        if (_registeredHandlerId <= 0 || !effectLoaded || !dynamicPreset)
        {
            throw new InvalidOperationException(
                $"Effect preset type code showcase requires registered handler '{HandlerKey}' and dynamic preset effect '{EffectId}'.");
        }

        runtime.SetMetricA("Calls", "0");
        runtime.SetMetricB("Preset", "dynamic");
        runtime.SetLastEvent("Heat Mark is ready and can be applied from the button.");
    }

    private void UpdateMetrics(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        runtime.SetHighlightRight(true);
        runtime.SetMetricA("Calls", _handlerCallCount.ToString());
        runtime.SetMetricB("Heat", $"stack {runtime.PrimaryActionCount}");
        if (_handlerCallCount > 0)
        {
            runtime.SetLastEvent("Heat Mark applied to the target and increased its call count.");
        }
    }

    private void RequestHeatMark(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        int effectId = EffectTemplateIdRegistry.GetId(EffectId);
        if (effectId <= 0)
        {
            throw new InvalidOperationException($"Effect '{EffectId}' is not registered.");
        }

        if (engine.GetService(CoreServiceKeys.EffectRequestQueue) is not EffectRequestQueue queue)
        {
            throw new InvalidOperationException("Effect preset type code showcase requires EffectRequestQueue.");
        }

        queue.Publish(new EffectRequest
        {
            Source = _targetEntity,
            Target = _targetEntity,
            TargetContext = _targetEntity,
            TemplateId = effectId
        });

        runtime.SetHighlightRight(true);
        runtime.SetMetricA("Calls", _handlerCallCount.ToString());
        runtime.SetMetricB("Heat", $"queued {runtime.PrimaryActionCount}");
        runtime.SetLastEvent("Heat Mark was queued and will apply on the next game tick.");
    }

    private void ApplyHeatMark(
        World world,
        Entity effectEntity,
        ref EffectContext context,
        in EffectConfigParams mergedParams,
        in EffectTemplateData templateData)
    {
        _handlerCallCount++;
    }

    private static Entity CreateTargetEntity(GameEngine engine)
    {
        return engine.World.Create(new VisualTransform
        {
            Position = new Vector3(11.4f, 0f, 5.2f),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        });
    }
}
