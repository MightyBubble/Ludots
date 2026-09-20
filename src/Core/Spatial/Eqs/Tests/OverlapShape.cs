namespace Ludots.Core.Spatial.Eqs.Tests
{
    /// <summary>Cast shape used by OverlapTest, mapping to ISpatialQueryService query functions.</summary>
    public enum OverlapShape : byte
    {
        /// <summary>QueryRadius — circle around candidate.</summary>
        Radius = 0,

        /// <summary>QueryCone — sector around candidate facing origin.</summary>
        Cone = 1,

        /// <summary>QueryRectangle — oriented box around candidate.</summary>
        Rectangle = 2,

        /// <summary>QueryLine — capsule from candidate toward origin.</summary>
        Line = 3
    }
}
