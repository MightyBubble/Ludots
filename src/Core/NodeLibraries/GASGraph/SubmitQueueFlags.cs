using System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Scripting;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Instruction <c>Flags</c> encoding of the submit ops' <c>queue</c> property. Omitted replaces
    /// the actors' current orders; the queue-modifier form reads the firing input action's held
    /// modifiers from the entry payload, so it only runs inside an input-action entry.
    /// </summary>
    public static class SubmitQueueFlags
    {
        public const string OnQueueModifierName = "onQueueModifier";
        public const string AlwaysName = "always";

        public const byte Immediate = 0;
        public const byte OnQueueModifier = 1;
        public const byte Always = 2;

        public static bool IsKnown(string value)
        {
            return string.Equals(value, OnQueueModifierName, StringComparison.Ordinal) ||
                   string.Equals(value, AlwaysName, StringComparison.Ordinal);
        }

        public static byte Encode(string? value)
        {
            if (value == null)
            {
                return Immediate;
            }

            return string.Equals(value, AlwaysName, StringComparison.Ordinal) ? Always : OnQueueModifier;
        }

        public static OrderSubmitMode Resolve(byte flags, GraphEntryPayloadTable? payload, string opName)
        {
            switch (flags)
            {
                case Immediate:
                    return OrderSubmitMode.Immediate;
                case Always:
                    return OrderSubmitMode.Queued;
                case OnQueueModifier:
                    if (payload == null || !payload.TryGetInt(MapTriggerEventPayloadKeys.Modifiers, out int modifiers))
                    {
                        throw new InvalidOperationException(
                            $"GAS.GRAPH.ERR.SubmitQueueModifierUnavailable: {opName} declares queue '{OnQueueModifierName}' but the entry that ran it is not an input action.");
                    }

                    return (modifiers & InputActionFiredModifiers.Queue) != 0
                        ? OrderSubmitMode.Queued
                        : OrderSubmitMode.Immediate;
                default:
                    throw new InvalidOperationException($"GAS.GRAPH.ERR.SubmitQueueFlagsInvalid: {opName} carries queue flags {flags}.");
            }
        }
    }
}
