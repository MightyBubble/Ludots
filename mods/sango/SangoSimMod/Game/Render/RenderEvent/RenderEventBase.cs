using Sango.Core;

namespace Sango.Render
{
    public class RenderEventBase : IRenderEventBase
    {
        public virtual bool IsInited { get; set; }
        public virtual bool IsDone { get; set; }
        public virtual bool IsVisible() => true;
        public virtual bool MarkDepends{ get; set; }
        public virtual bool Update(Scenario scenario, float deltaTime)
        {
            return IsDone;
        }
        public virtual void Enter(Scenario scenario) { }
        public virtual void Exit(Scenario scenario) { }
    }
}
