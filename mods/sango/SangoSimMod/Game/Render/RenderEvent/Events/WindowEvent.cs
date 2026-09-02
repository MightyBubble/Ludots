using Sango.Core;

namespace Sango.Render
{
    public class WindowEvent : RenderEventBase
    {
        public string windowName;
        public object[] args;

        Window.WindowInterface targetWindow;

        public void Init(string windowName, object[] args)
        {
            this.windowName = windowName;
            this.args = args;
            IsDone = false;
        }
        public override void Enter(Scenario scenario)
        {
            targetWindow = Window.Instance.Open(windowName, args);
            if (targetWindow == null)
            {
                IsDone = true; 
                return;
            }
            targetWindow.ugui_instance.OnCloseAction = OnWindowHide;
        }

        void OnWindowHide()
        {
            targetWindow.ugui_instance.OnCloseAction = null;
            IsDone = true;
        }
    }
}
