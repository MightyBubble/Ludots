using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public enum TargetShape : byte
    {
        Self = 0,
        Single = 1,   // Requires explicit target selection from input
        Circle = 2,   // AOE around cursor or self
        Cone = 3,     // Cone in a direction
        Line = 4,     // Line skillshot (from caster in a direction)
        Ring = 5,     // Ring (donut) AOE
        Rectangle = 6 // Rectangular area (e.g. vector-targeted skills)
    }

    /// <summary>
    /// Defines how the ability selects targets.
    /// </summary>
    public struct TargetSelector
    {
        public TargetShape Shape;
        public RelationshipFilter Filter;
        public byte Flags;
        
        public float Range;  // Cast Range
        public float Radius; // AOE Radius (for Circle/Cone)
        public float Angle;  // Cone Angle
        
        public bool ExcludeSelf
        {
            readonly get => (Flags & 1) != 0;
            set => Flags = value ? (byte)(Flags | 1) : (byte)(Flags & 0xFE);
        }
    }
}
