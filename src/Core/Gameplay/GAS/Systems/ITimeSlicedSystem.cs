namespace Ludots.Core.Gameplay.GAS.Systems
{
    public interface ITimeSlicedSystem
    {
        bool UpdateSlice(float dt, int timeBudgetMs);
        void ResetSlice();
    }
}
