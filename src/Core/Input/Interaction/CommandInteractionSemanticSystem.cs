using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Interaction;

public sealed class CommandInteractionSemanticSystem : ISystem<float>
{
    private readonly Dictionary<string, object> _globals;

    public CommandInteractionSemanticSystem(Dictionary<string, object> globals)
    {
        _globals = globals;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object? inputObj) ||
            inputObj is not IInputActionReader input)
        {
            CommandInteractionSemanticRuntime.Publish(
                _globals,
                new CommandInteractionSemanticSnapshot(CommandInteractionSemanticKind.None, commandPressedThisFrame: false));
            return;
        }

        InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(
            _globals,
            nameof(CommandInteractionSemanticSystem));
        CommandInteractionSemanticSnapshot snapshot = CommandInteractionSemanticRuntime.Resolve(_globals, input, bindings);
        CommandInteractionSemanticRuntime.Publish(_globals, in snapshot);
    }
}
