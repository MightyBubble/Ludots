using Sango.Core;

namespace Sango.Render
{
    public class DelayEvent : RenderEventBase
    {
        public float delayTime;
        public System.Action doneAction;
        public void Init(float delayTime, System.Action action)
        {
            IsDone = false; 
            this.delayTime = delayTime; 
            doneAction = action;
        }

        public override void Exit(Scenario scenario)
        {
            base.Exit(scenario);
            doneAction?.Invoke();
        }

        public override bool Update(Scenario scenario, float deltaTime)
        {
            delayTime -= deltaTime;
            IsDone = delayTime <= 0;
            return IsDone;
        }
    }
}
