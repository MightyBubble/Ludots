namespace Ludots.Core.Engine.Pacemaker
{
    public interface ICooperativeSimulation
    {
        bool Step(float fixedDt, int timeBudgetMs);
        void Reset();
    }
}
