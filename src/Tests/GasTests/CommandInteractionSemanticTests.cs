using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class CommandInteractionSemanticTests
{
    [Test]
    public void SemanticSystem_AimState_ShadowsGroundMove()
    {
        var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
        var bindings = new InteractionActionBindings();
        InputOrderMappingSystem mapping = CreateAimingMapping(input, bindings);
        var globals = CreateGlobals(input, bindings, mapping);
        var system = new CommandInteractionSemanticSystem(globals);

        input.InjectButtonPress(bindings.CommandActionId);
        input.Update();
        system.Update(0f);

        Assert.That(CommandInteractionSemanticRuntime.TryRead(globals, out CommandInteractionSemanticSnapshot snapshot), Is.True);
        Assert.That(snapshot.Kind, Is.EqualTo(CommandInteractionSemanticKind.CancelAim));
        Assert.That(
            CommandInteractionSemanticRuntime.TryConsumeGroundMoveCommand(globals, out WorldCmInt2 _),
            Is.False);
    }

    [Test]
    public void SemanticSystem_GameplayState_AllowsGroundMove()
    {
        var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
        var bindings = new InteractionActionBindings();
        var globals = CreateGlobals(input, bindings, mapping: null);
        var system = new CommandInteractionSemanticSystem(globals);
        SetAuthoritativeGroundPoint(input, new WorldCmInt2(1600, 1200));

        input.InjectButtonPress(bindings.CommandActionId);
        input.Update();
        system.Update(0f);

        Assert.That(CommandInteractionSemanticRuntime.TryRead(globals, out CommandInteractionSemanticSnapshot snapshot), Is.True);
        Assert.That(snapshot.Kind, Is.EqualTo(CommandInteractionSemanticKind.GroundMove));
        Assert.That(
            CommandInteractionSemanticRuntime.TryConsumeGroundMoveCommand(globals, out WorldCmInt2 worldCm),
            Is.True);
        Assert.That(worldCm.X, Is.EqualTo(1600));
        Assert.That(worldCm.Y, Is.EqualTo(1200));
    }

    [Test]
    public void TryConsumeGroundMoveCommand_WithoutSemanticSystem_RejectsGroundMove()
    {
        var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
        var bindings = new InteractionActionBindings();
        InputOrderMappingSystem mapping = CreateAimingMapping(input, bindings);
        var globals = CreateGlobals(input, bindings, mapping);

        input.InjectButtonPress(bindings.CommandActionId);
        input.Update();

        Assert.That(
            CommandInteractionSemanticRuntime.TryConsumeGroundMoveCommand(globals, out WorldCmInt2 _),
            Is.False);
    }

    private static Dictionary<string, object> CreateGlobals(
        PlayerInputHandler input,
        InteractionActionBindings bindings,
        InputOrderMappingSystem? mapping)
    {
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.AuthoritativeInput.Name] = input,
            [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
            [CoreServiceKeys.InteractionActionBindings.Name] = bindings,
        };

        if (mapping != null)
        {
            globals[CoreServiceKeys.ActiveInputOrderMapping.Name] = mapping;
        }

        return globals;
    }

    private static InputOrderMappingSystem CreateAimingMapping(PlayerInputHandler input, InteractionActionBindings bindings)
    {
        var mapping = new InputOrderMappingSystem(input, new InputOrderMappingConfig
        {
            InteractionMode = InteractionModeType.AimCast,
            Mappings = new List<InputOrderMapping>
            {
                new()
                {
                    ActionId = "SkillQ",
                    Trigger = InputTriggerType.PressedThisFrame,
                    OrderTypeKey = "castAbility",
                    ArgsTemplate = new OrderArgsTemplate { I0 = 0 },
                    RequireSelection = false,
                    SelectionType = OrderSelectionType.Entity,
                    IsSkillMapping = true,
                },
            },
        });
        mapping.SetInteractionActionBindings(bindings);
        mapping.SetOrderTypeKeyResolver(key => key == "castAbility" ? 1001 : 0);
        mapping.SetSelectedEntityProvider((string _, out Entity entity) =>
        {
            entity = default;
            return false;
        });
        mapping.SetOrderSubmitHandler((in Order _) => { });

        input.InjectButtonPress("SkillQ");
        input.Update();
        mapping.Update(0f);
        Assert.That(mapping.IsAiming, Is.True);
        return mapping;
    }

    private static void SetAuthoritativeGroundPoint(PlayerInputHandler input, in WorldCmInt2 worldCm)
    {
        input.InjectAction(AuthoritativeGroundPointerHelper.ActionId, new System.Numerics.Vector3(worldCm.X, 0f, worldCm.Y));
    }

    private static InputConfigRoot CreateInputConfig()
    {
        return new InputConfigRoot
        {
            Actions = new List<InputActionDef>
            {
                new() { Id = "SkillQ", Name = "SkillQ", Type = InputActionType.Button },
                new() { Id = "Command", Name = "Command", Type = InputActionType.Button },
                new() { Id = "Confirm", Name = "Confirm", Type = InputActionType.Button },
                new() { Id = AuthoritativeGroundPointerHelper.ActionId, Name = AuthoritativeGroundPointerHelper.ActionId, Type = InputActionType.Axis3D },
            },
            Contexts = new List<InputContextDef>
            {
                new() { Id = "Test", Name = "Test", Priority = 1 },
            },
        };
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
