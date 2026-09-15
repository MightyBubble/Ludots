using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardPresenterMaterialBehaviorShowcaseMod.Runtime;

public sealed class CapabilityStandardPresenterMaterialBehaviorShowcaseSystem : ISystem<float>
{
    public const string MapId = "capability_standard_presenter_material_behavior_showcase";
    public const string PropDefinitionKey = "material_behavior_showcase.prop";
    public const string SelectCoolActionId = "MaterialBehaviorShowcase.SelectCool";
    public const string SelectWarmActionId = "MaterialBehaviorShowcase.SelectWarm";
    public const string ToggleActionId = "MaterialBehaviorShowcase.Toggle";

    private const int PropOwnerStableId = 61901;
    private static readonly Vector3 PropPosition = new(0f, 0.7f, 0f);

    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly PlayerInputHandler _input;
    private readonly PresenterCommandBuffer _presenterCommands;
    private readonly int _propDefinitionId;
    private readonly int _coolKeyId;
    private readonly int _warmKeyId;

    private Entity _propOwner = Entity.Null;
    private bool _warmSelected;

    public CapabilityStandardPresenterMaterialBehaviorShowcaseSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _world = engine.World;
        _input = engine.GetService(CoreServiceKeys.InputHandler)
            ?? throw new InvalidOperationException("Material behavior showcase requires InputHandler.");
        _presenterCommands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer)
            ?? throw new InvalidOperationException("Material behavior showcase requires PresenterCommandBuffer.");
        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("Material behavior showcase requires PresenterDefinitionRegistry.");
        _propDefinitionId = definitions.GetId(PropDefinitionKey);
        if (_propDefinitionId <= 0)
        {
            throw new InvalidOperationException(
                $"Material behavior showcase presenter definition '{PropDefinitionKey}' is not registered.");
        }

        _coolKeyId = TagRegistry.Register("material_behavior_showcase.swap.cool");
        _warmKeyId = TagRegistry.Register("material_behavior_showcase.swap.warm");
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, MapId, StringComparison.Ordinal))
        {
            return;
        }

        EnsureShowcaseSpawned();
        PollActions();
    }

    private void EnsureShowcaseSpawned()
    {
        if (_propOwner != Entity.Null)
        {
            return;
        }

        _propOwner = CreateOwner(PropOwnerStableId, PropPosition);
        EnqueuePresenterCreate(_propDefinitionId, "material_behavior_showcase.prop", _propOwner);
    }

    private void PollActions()
    {
        if (_input.PressedThisFrame(SelectCoolActionId))
        {
            _warmSelected = false;
            PublishGameplayEvent(_coolKeyId, _propOwner);
            return;
        }

        if (_input.PressedThisFrame(SelectWarmActionId))
        {
            _warmSelected = true;
            PublishGameplayEvent(_warmKeyId, _propOwner);
            return;
        }

        if (_input.PressedThisFrame(ToggleActionId))
        {
            _warmSelected = !_warmSelected;
            PublishGameplayEvent(_warmSelected ? _warmKeyId : _coolKeyId, _propOwner);
        }
    }

    private Entity CreateOwner(int stableId, Vector3 position)
    {
        return _world.Create(
            new VisualTransform
            {
                Position = position,
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            },
            new PresentationStableId { Value = stableId });
    }

    private void EnqueuePresenterCreate(int definitionId, string scopeTagName, Entity source)
    {
        if (!_presenterCommands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
                PresenterDefinitionId = definitionId,
                ScopeTag = PresenterScopeTagRegistry.Register(scopeTagName),
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = source,
            }))
        {
            throw new InvalidOperationException("PresenterCommandBuffer overflowed while creating the Material behavior showcase hierarchy.");
        }
    }

    private void PublishGameplayEvent(int keyId, Entity source)
    {
        if (_engine.GetService(CoreServiceKeys.PresentationEventStream) is not PresentationEventStream events)
        {
            throw new InvalidOperationException("Material behavior showcase requires PresentationEventStream.");
        }

        if (!events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.GameplayEvent,
                KeyId = keyId,
                Source = source,
                Target = source,
                Position = Vector3.Zero,
            }))
        {
            throw new InvalidOperationException("PresentationEventStream overflowed while publishing the Material behavior showcase event.");
        }
    }

    public void Dispose()
    {
    }
}
