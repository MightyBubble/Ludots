using System;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Instruction encoding of SubmitCommandIntent's <c>layout</c> / <c>layoutSpacingCm</c>:
    /// the assignment rides in <c>C</c>, the spacing in <c>Imm</c>.
    /// </summary>
    public static class SubmitGroundLayout
    {
        public const string PreserveRelativeName = "preserveRelative";
        public const string ActorOrderName = "actorOrder";

        public static bool IsKnown(string value)
        {
            return string.Equals(value, PreserveRelativeName, StringComparison.Ordinal) ||
                   string.Equals(value, ActorOrderName, StringComparison.Ordinal);
        }

        public static byte Encode(string? value)
        {
            if (value == null)
            {
                return (byte)GroundLayoutAssignment.None;
            }

            return string.Equals(value, PreserveRelativeName, StringComparison.Ordinal)
                ? (byte)GroundLayoutAssignment.PreserveRelative
                : (byte)GroundLayoutAssignment.ActorOrder;
        }

        public static GroundLayout Decode(byte assignment, int spacingCm)
        {
            var decoded = (GroundLayoutAssignment)assignment;
            if (decoded == GroundLayoutAssignment.None)
            {
                return default;
            }

            if (!Enum.IsDefined(decoded) || spacingCm <= 0)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.SubmitGroundLayoutInvalid: SubmitCommandIntent carries layout {assignment} with spacing {spacingCm}.");
            }

            return new GroundLayout(decoded, spacingCm);
        }
    }
}
