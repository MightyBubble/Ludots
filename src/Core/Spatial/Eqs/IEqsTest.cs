namespace Ludots.Core.Spatial.Eqs
{
    /// <summary>
    /// Test/score function modifies EqsItem score or filters it out.
    /// </summary>
    public interface IEqsTest
    {
        /// <summary>
        /// Evaluate <paramref name="item"/> and update its Score or Filtered flag.
        /// </summary>
        void Score(in EqsContext ctx, ref EqsItem item);
    }
}
