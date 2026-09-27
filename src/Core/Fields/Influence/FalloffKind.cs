namespace Ludots.Core.Fields.Influence
{
    /// <summary>Falloff shape for influence stamp projection.</summary>
    public enum FalloffKind : byte
    {
        /// <summary>Flat constant within radius.</summary>
        Constant = 0,
        
        /// <summary>Linear decay: peak * (1 - distance/radius)</summary>
        Linear = 1,
        
        /// <summary>Quadratic decay: peak * (1 - distance/radius)^2</summary>
        Quadratic = 2
    }
}
