using System.Numerics;
using Arch.Core;
using CapabilityStandardModExtensibleRuntimeShowcaseShared;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;

namespace CapabilityStandardPerformerBehaviorExtensionShowcaseMod;

public sealed class CapabilityStandardPerformerBehaviorExtensionShowcaseModEntry : IMod
{
    public const string BehaviorKey = "CapabilityStandardPerformerBehaviorExtensionShowcaseMod.CloudDrift";
    public const string PerformerDefinitionKey = "capability_standard.performer_behavior_extension.cloud_banner";
    private const string MapId = "capability_standard_performer_behavior_extension_showcase";
    private const string DriftParamKey = "capability_standard.performer_behavior_extension.drift";
    private int _registeredBehaviorId;
    private int _driftParamId;
    private int _behaviorTickCount;
    private Entity _ownerEntity = Entity.Null;

    public void OnLoad(IModContext context)
    {
        _registeredBehaviorId = context.Extensions.Presentation.RegisterPerformerBehavior(
            BehaviorKey,
            new PerformerBehaviorExtensionDescriptor(
                PerformerBehaviorExecutionLane.ContinuousTick,
                RunCloudDrift));
        _driftParamId = PerformerParamKeyRegistry.Register(DriftParamKey);

        var runtime = new ExtensibleRuntimeShowcaseRuntime(new ExtensibleRuntimeShowcaseScenario
        {
            MapId = MapId,
            PanelElementId = "capability-standard-performer-behavior-extension-panel",
            PrimaryButtonElementId = "capability-standard-performer-behavior-extension-focus",
            SurfaceOwnerId = "Showcase.CapabilityStandardPerformerBehaviorExtension.Panel",
            Title = "Performer Behavior Extension",
            FeatureLabel = "Behavior kind",
            PrimaryButtonLabel = "Focus Cloud Drift",
            AccentColor = "#57D9D2",
            ReadyText = "CloudDrift keeps moving through a Mod behavior.",
            ProofLines =
            [
                $"Behavior key: {BehaviorKey}",
                "Performer shard: Presentation/performers/capability_standard.performer_behavior_extension.cloud_banner.json",
                "The performer runtime ticks the Mod behavior through the registered dynamic kind."
            ],
            OnActivated = ActivateShowcase,
            OnUpdate = UpdateMetrics,
            OnPrimaryAction = FocusCloudDrift
        });

        ExtensibleRuntimeShowcaseBootstrap.Install(context, runtime, nameof(CapabilityStandardPerformerBehaviorExtensionShowcaseMod));
    }

    public void OnUnload()
    {
    }

    private void ActivateShowcase(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        _behaviorTickCount = 0;
        _ownerEntity = CreateOwnerEntity(engine);

        int definitionId = VerifyDefinitionLoaded(engine);
        EnqueuePerformerCreate(engine, definitionId);

        runtime.SetMetricA("Behavior", _registeredBehaviorId > 0 ? $"id {_registeredBehaviorId}" : "missing");
        runtime.SetMetricB("Ticks", "0");
        runtime.SetLastEvent("CloudDrift has appeared and should keep running.");
    }

    private void UpdateMetrics(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        runtime.SetMetricA("Behavior", _registeredBehaviorId > 0 ? $"id {_registeredBehaviorId}" : "missing");
        runtime.SetMetricB("Ticks", _behaviorTickCount.ToString());
        if (_behaviorTickCount > 0)
        {
            runtime.SetLastEvent("CloudDrift is running and its tick count is growing.");
        }
    }

    private void FocusCloudDrift(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        runtime.SetHighlightRight(runtime.PrimaryActionCount % 2 == 0);
        runtime.SetMetricA("Behavior", _registeredBehaviorId > 0 ? $"id {_registeredBehaviorId}" : "missing");
        runtime.SetMetricB("Ticks", _behaviorTickCount.ToString());
        runtime.SetLastEvent("Focus moved to CloudDrift while it kept running.");
    }

    private int VerifyDefinitionLoaded(GameEngine engine)
    {
        var definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
            ?? throw new InvalidOperationException("Performer behavior extension showcase requires PerformerDefinitionRegistry.");
        int definitionId = definitions.GetId(PerformerDefinitionKey);
        if (definitionId <= 0 || !definitions.TryGet(definitionId, out PerformerDefinition definition))
        {
            throw new InvalidOperationException($"Performer definition '{PerformerDefinitionKey}' is not registered.");
        }

        if (definition.Behaviors.Length != 1 ||
            definition.Behaviors[0].Kind != BehaviorKind.Extension ||
            definition.Behaviors[0].KindId != _registeredBehaviorId)
        {
            throw new InvalidOperationException($"Performer definition '{PerformerDefinitionKey}' did not compile to the registered behavior extension.");
        }

        return definitionId;
    }

    private static Entity CreateOwnerEntity(GameEngine engine)
    {
        return engine.World.Create(
            new VisualTransform
            {
                Position = new Vector3(7.4f, 0f, 5.2f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One
            },
            new PresentationStableId { Value = 50101 });
    }

    private void EnqueuePerformerCreate(GameEngine engine, int definitionId)
    {
        if (engine.GetService(CoreServiceKeys.PerformerCommandBuffer) is not PerformerCommandBuffer commands)
        {
            throw new InvalidOperationException("Performer behavior extension showcase requires PerformerCommandBuffer.");
        }

        if (!commands.TryAdd(new PerformerCommand
        {
            CommandKind = PerformerCommandKind.CreatePerformer,
            CommandKindId = (byte)PerformerCommandKind.CreatePerformer,
            RouteStrategy = PerformerCommandRouteStrategy.CreatePerformer,
            PerformerDefinitionId = definitionId,
            ScopeTag = 50101,
            ScopeSource = PerformerCommandScopeSource.Fixed,
            AnchorKind = PresentationAnchorKind.Entity,
            Source = _ownerEntity
        }))
        {
            throw new InvalidOperationException("PerformerCommandBuffer overflowed while creating the behavior extension showcase performer.");
        }
    }

    private void RunCloudDrift(in PerformerBehaviorExecutionContext context)
    {
        _behaviorTickCount++;
        context.Ops.SetParam(_driftParamId, ParamLane.Float, _behaviorTickCount);
    }
}
