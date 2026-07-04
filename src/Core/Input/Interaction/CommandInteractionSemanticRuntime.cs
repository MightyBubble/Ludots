using System.Collections.Generic;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Interaction;

public static class CommandInteractionSemanticRuntime
{
    public static bool TryRead(
        IReadOnlyDictionary<string, object> globals,
        out CommandInteractionSemanticSnapshot snapshot)
    {
        if (globals.TryGetValue(CoreServiceKeys.CommandInteractionSemantic.Name, out object? raw) &&
            raw is CommandInteractionSemanticSnapshot published)
        {
            snapshot = published;
            return true;
        }

        snapshot = default;
        return false;
    }

    public static bool TryConsumeGroundMoveCommand(
        IReadOnlyDictionary<string, object> globals,
        out WorldCmInt2 worldCm)
    {
        worldCm = default;
        if (!globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object? inputObj) ||
            inputObj is not IInputActionReader input)
        {
            return false;
        }

        InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(
            globals,
            nameof(CommandInteractionSemanticRuntime));
        if (!input.PressedThisFrame(bindings.CommandActionId))
        {
            return false;
        }

        if (TryRead(globals, out CommandInteractionSemanticSnapshot snapshot))
        {
            if (snapshot.Kind != CommandInteractionSemanticKind.GroundMove)
            {
                return false;
            }
        }
        else if (IsCommandShadowedByAim(globals))
        {
            return false;
        }

        return AuthoritativeGroundPointerHelper.TryRead(input, out worldCm);
    }

    internal static void Publish(Dictionary<string, object> globals, in CommandInteractionSemanticSnapshot snapshot)
    {
        globals[CoreServiceKeys.CommandInteractionSemantic.Name] = snapshot;
    }

    internal static CommandInteractionSemanticSnapshot Resolve(
        IReadOnlyDictionary<string, object> globals,
        IInputActionReader input,
        InteractionActionBindings bindings)
    {
        bool commandPressed = input.PressedThisFrame(bindings.CommandActionId);
        if (!commandPressed)
        {
            return new CommandInteractionSemanticSnapshot(CommandInteractionSemanticKind.None, commandPressedThisFrame: false);
        }

        CommandInteractionSemanticKind kind = IsCommandShadowedByAim(globals)
            ? CommandInteractionSemanticKind.CancelAim
            : CommandInteractionSemanticKind.GroundMove;
        return new CommandInteractionSemanticSnapshot(kind, commandPressedThisFrame: true);
    }

    private static bool IsCommandShadowedByAim(IReadOnlyDictionary<string, object> globals)
    {
        return globals.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out object? mappingObj) &&
               mappingObj is InputOrderMappingSystem mapping &&
               mapping.IsAiming;
    }
}
