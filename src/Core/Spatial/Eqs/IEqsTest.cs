namespace Ludots.Core.Spatial.Eqs
{
    public interface IEqsTest
    {
        void Score(in EqsContext ctx, ref EqsItem item);
    }
}
