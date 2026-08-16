using System.Numerics;
using Arch.Core;
using CapabilityStandardModExtensibleRuntimeShowcaseShared;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;

namespace CapabilityStandardPerformerCommandExtensionShowcaseMod;

public sealed class CapabilityStandardPerformerCommandExtensionShowcaseModEntry : IMod
{
    public const string CommandKey = "CapabilityStandardPerformerCommandExtensionShowcaseMod.EmitSignalPing";
    public const string PresenterDefinitionKey = "capability_standard.performer_command_extension.signal_rules";
    public const string EventKey = "CapabilityStandard.PerformerCommandExtension.Signal";
    private const string MapId = "capability_standard_performer_command_extension_showcase";
    private int _registeredCommandId;
    private int _signalEventId;
    private int _commandCallCount;
    private int _lastPayload;
    private int _lastScopeTag;
    private bool _lastHadRoutedPresenter;
    private Entity _ownerEntity = Entity.Null;

    public void OnLoad(IModContext context)
    {
        _registeredCommandId = context.Extensions.Presentation.RegisterPresenterCommand(
            CommandKey,
            new PresenterCommandExtensionDescriptor(
                PresenterCommandRouteStrategy.ExistingInstances,
                EmitSignalPing));
        _signalEventId = TagRegistry.Register(EventKey);

        var runtime = new ExtensibleRuntimeShowcaseRuntime(new ExtensibleRuntimeShowcaseScenario
        {
            MapId = MapId,
            PanelElementId = "capability-standard-performer-command-extension-panel",
            PrimaryButtonElementId = "capability-standard-performer-command-extension-signal",
            SurfaceOwnerId = "Showcase.CapabilityStandardPerformerCommandExtension.Panel",
            Title = "Presenter Command Extension",
            FeatureLabel = "Command kind",
            PrimaryButtonLabel = "Send Signal Ping",
            AccentColor = "#F2C94C",
            ReadyText = "Signal Ping is routed to the existing presenter.",
            ProofLines =
            [
                $"Command key: {CommandKey}",
                $"Gameplay event: {EventKey}",
                "The button publishes an event; presenter rules produce the extension command."
            ],
            OnActivated = ActivateShowcase,
            OnUpdate = UpdateMetrics,
            OnPrimaryAction = PublishSignal
        });

        ExtensibleRuntimeShowcaseBootstrap.Install(context, runtime, nameof(CapabilityStandardPerformerCommandExtensionShowcaseMod));
    }

    public void OnUnload()
    {
    }

    private void ActivateShowcase(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        _commandCallCount = 0;
        _lastPayload = 0;
        _lastScopeTag = 0;
        _lastHadRoutedPresenter = false;
        _ownerEntity = CreateOwnerEntity(engine);
        int definitionId = VerifyDefinitionLoaded(engine);
        EnqueuePresenterCreate(engine, definitionId);

        runtime.SetMetricA("Command", _registeredCommandId > 0 ? $"id {_registeredCommandId}" : "missing");
        runtime.SetMetricB("Handled", "0");
        runtime.SetLastEvent("A presenter is ready; press the signal button to ping it.");
    }

    private void UpdateMetrics(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        runtime.SetMetricA("Command", _registeredCommandId > 0 ? $"id {_registeredCommandId}" : "missing");
        runtime.SetMetricB("Handled", _commandCallCount.ToString());
        if (_commandCallCount > 0)
        {
            string route = _lastHadRoutedPresenter ? "existing presenter" : "unknown presenter";
            runtime.SetLastEvent($"Signal #{_lastPayload} was handled by the {route}.");
        }
    }

    private void PublishSignal(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.PresentationEventStream) is not PresentationEventStream events)
        {
            throw new InvalidOperationException("Presenter command extension showcase requires PresentationEventStream.");
        }

        int payload = runtime.PrimaryActionCount;
        if (!events.TryAdd(new PresentationEvent
        {
            Kind = PresentationEventKind.GameplayEvent,
            KeyId = _signalEventId,
            Source = _ownerEntity,
            Target = _ownerEntity,
            PayloadA = payload,
            Magnitude = payload,
            Position = new Vector3(11.4f, 0f, 5.2f)
        }))
        {
            throw new InvalidOperationException("PresentationEventStream overflowed while publishing the command extension showcase signal.");
        }

        runtime.SetHighlightRight(payload % 2 == 1);
        runtime.SetMetricA("Signal", $"#{payload}");
        runtime.SetMetricB("Handled", _commandCallCount.ToString());
        runtime.SetLastEvent("Signal Ping was sent and will be handled on the next frame.");
    }

    private int VerifyDefinitionLoaded(GameEngine engine)
    {
        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("Presenter command extension showcase requires PresenterDefinitionRegistry.");
        int definitionId = definitions.GetId(PresenterDefinitionKey);
        if (definitionId <= 0 || !definitions.TryGet(definitionId, out PresenterDefinition definition))
        {
            throw new InvalidOperationException($"Presenter definition '{PresenterDefinitionKey}' is not registered.");
        }

        if (definition.Rules.Length != 1 ||
            definition.Rules[0].Event.Kind != PresentationEventKind.GameplayEvent ||
            definition.Rules[0].Event.KeyId != _signalEventId ||
            definition.Rules[0].Command.CommandKind != PresenterCommandKind.Extension ||
            definition.Rules[0].Command.CommandKindId != _registeredCommandId ||
            definition.Rules[0].Command.RouteStrategy != PresenterCommandRouteStrategy.ExistingInstances)
        {
            throw new InvalidOperationException($"Presenter definition '{PresenterDefinitionKey}' did not compile to the registered command extension.");
        }

        return definitionId;
    }

    private static Entity CreateOwnerEntity(GameEngine engine)
    {
        return engine.World.Create(
            new VisualTransform
            {
                Position = new Vector3(11.4f, 0f, 5.2f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One
            },
            new PresentationStableId { Value = 50201 });
    }

    private void EnqueuePresenterCreate(GameEngine engine, int definitionId)
    {
        if (engine.GetService(CoreServiceKeys.PresenterCommandBuffer) is not PresenterCommandBuffer commands)
        {
            throw new InvalidOperationException("Presenter command extension showcase requires PresenterCommandBuffer.");
        }

        if (!commands.TryAdd(new PresenterCommand
        {
            CommandKind = PresenterCommandKind.CreatePresenter,
            CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
            RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
            PresenterDefinitionId = definitionId,
            ScopeTag = 50201,
            ScopeSource = PresenterCommandScopeSource.Fixed,
            AnchorKind = PresentationAnchorKind.Entity,
            Source = _ownerEntity
        }))
        {
            throw new InvalidOperationException("PresenterCommandBuffer overflowed while creating the command extension showcase presenter.");
        }
    }

    private void EmitSignalPing(in PresenterCommandExecutionContext context)
    {
        if (!context.Ops.HasRoutedPresenter)
        {
            throw new InvalidOperationException("EmitSignalPing requires an ExistingInstances-routed presenter.");
        }

        _commandCallCount++;
        _lastPayload = context.Command.IntValue;
        _lastScopeTag = context.Command.ScopeTag;
        _lastHadRoutedPresenter = true;
        context.Ops.SetParam(
            context.Command.ParamKey,
            context.Command.ParamLane,
            intValue: context.Command.IntValue);
    }
}
