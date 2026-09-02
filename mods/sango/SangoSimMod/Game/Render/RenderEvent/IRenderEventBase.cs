using Sango.Core;

namespace Sango.Render
{
    public interface IRenderEventBase
    {
        bool IsInited { get; set; }
        bool IsDone { get; set; }
        bool MarkDepends { get; set; }

        bool IsVisible();
        bool Update(Scenario scenario, float deltaTime);
        void Enter(Scenario scenario);
        void Exit(Scenario scenario);
    }
}
