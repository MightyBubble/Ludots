using System.Numerics;
using Arch.Core;
using CapabilityStandardModExtensibleRuntimeShowcaseShared;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;

namespace CapabilityStandardPresenterBehaviorExtensionShowcaseMod;

public sealed class CapabilityStandardPresenterBehaviorExtensionShowcaseModEntry : IMod
{
    public const string BehaviorKey = "CapabilityStandardPresenterBehaviorExtensionShowcaseMod.CloudDrift";
    public const string PresenterDefinitionKey = "capability_standard.presenter_behavior_extension.cloud_banner";
    private const string MapId = "capability_standard_presenter_behavior_extension_showcase";
    private const string DriftParamKey = "capability_standard.presenter_behavior_extension.drift";
    private int _registeredBehaviorId;
    private int _driftParamId;
    private int _behaviorTickCount;
    private Entity _ownerEntity = Entity.Null;

    public void OnLoad(IModContext context)
    {
        _registeredBehaviorId = context.Extensions.Presentation.RegisterPresenterBehavior(
            BehaviorKey,
            new PresenterBehaviorExtensionDescriptor(
                PresenterBehaviorExecutionLane.ContinuousTick,
                RunCloudDrift));
        _driftParamId = PresenterParamKeyRegistry.Register(DriftParamKey);

        var runtime = new ExtensibleRuntimeShowcaseRuntime(new ExtensibleRuntimeShowcaseScenario
        {
            MapId = MapId,
            PanelElementId = "capability-standard-presenter-behavior-extension-panel",
            PrimaryButtonElementId = "capability-standard-presenter-behavior-extension-focus",
            SurfaceOwnerId = "Showcase.CapabilityStandardPresenterBehaviorExtension.Panel",
            Title = "Presenter Behavior Extension",
            FeatureLabel = "Behavior kind",
            PrimaryButtonLabel = "Focus Cloud Drift",
            AccentColor = "#57D9D2",
            ReadyText = "CloudDrift keeps moving through a Mod behavior.",
            ProofLines =
            [
                $"Behavior key: {BehaviorKey}",
                "Presenter shard: Presentation/presenters/capability_standard.presenter_behavior_extension.cloud_banner.json",
                "The presenter runtime ticks the Mod behavior through the registered dynamic kind."
            ],
            OnActivated = ActivateShowcase,
            OnUpdate = UpdateMetrics,
            OnPrimaryAction = FocusCloudDrift
        });

        ExtensibleRuntimeShowcaseBootstrap.Install(context, runtime, nameof(CapabilityStandardPresenterBehaviorExtensionShowcaseMod));
    }

    public void OnUnload()
    {
    }

    private void ActivateShowcase(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        _behaviorTickCount = 0;
        _ownerEntity = CreateOwnerEntity(engine);

        int definitionId = VerifyDefinitionLoaded(engine);
        EnqueuePresenterCreate(engine, definitionId);

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
        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("Presenter behavior extension showcase requires PresenterDefinitionRegistry.");
        int definitionId = definitions.GetId(PresenterDefinitionKey);
        if (definitionId <= 0 || !definitions.TryGet(definitionId, out PresenterDefinition definition))
        {
            throw new InvalidOperationException($"Presenter definition '{PresenterDefinitionKey}' is not registered.");
        }

        if (definition.Behaviors.Length != 1 ||
            definition.Behaviors[0].Kind != BehaviorKind.Extension ||
            definition.Behaviors[0].KindId != _registeredBehaviorId)
        {
            throw new InvalidOperationException($"Presenter definition '{PresenterDefinitionKey}' did not compile to the registered behavior extension.");
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

    private void EnqueuePresenterCreate(GameEngine engine, int definitionId)
    {
        if (engine.GetService(CoreServiceKeys.PresenterCommandBuffer) is not PresenterCommandBuffer commands)
        {
            throw new InvalidOperationException("Presenter behavior extension showcase requires PresenterCommandBuffer.");
        }

        if (!commands.TryAdd(new PresenterCommand
        {
            CommandKind = PresenterCommandKind.CreatePresenter,
            CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
            RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
            PresenterDefinitionId = definitionId,
            ScopeTag = 50101,
            ScopeSource = PresenterCommandScopeSource.Fixed,
            AnchorKind = PresentationAnchorKind.Entity,
            Source = _ownerEntity
        }))
        {
            throw new InvalidOperationException("PresenterCommandBuffer overflowed while creating the behavior extension showcase presenter.");
        }
    }

    private void RunCloudDrift(in PresenterBehaviorExecutionContext context)
    {
        _behaviorTickCount++;
        context.Ops.SetParam(_driftParamId, ParamLane.Float, _behaviorTickCount);
    }
}
