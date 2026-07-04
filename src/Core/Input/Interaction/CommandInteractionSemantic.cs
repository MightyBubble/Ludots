namespace Ludots.Core.Input.Interaction;

public enum CommandInteractionSemanticKind : byte
{
    None = 0,
    CancelAim = 1,
    GroundMove = 2,
}

public readonly struct CommandInteractionSemanticSnapshot
{
    public CommandInteractionSemanticSnapshot(CommandInteractionSemanticKind kind, bool commandPressedThisFrame)
    {
        Kind = kind;
        CommandPressedThisFrame = commandPressedThisFrame;
    }

    public CommandInteractionSemanticKind Kind { get; }
    public bool CommandPressedThisFrame { get; }
}
