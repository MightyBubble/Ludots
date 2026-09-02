using System.Collections.Generic;

using Sango.UI;

namespace Sango.Core.Player
{
    [GameSystem]
    public class ObjectsDisplaySystem : GameSystem
    {
        public List<SangoObject> Objects;
        public string customSortTitleName;
        public List<ObjectSortTitle> customSortItems;
        public string windowName = "window_object_selector";

        public struct ButtonData
        {
            public string title;
            public int style;
            public System.Action action;
        }

        public List<ButtonData> buttonDatas;

        /// <summary>
        /// 点选模式
        /// </summary>
        public bool ClickMode { get; set; }

        public void Start(List<SangoObject> sangoObjects, List<ObjectSortTitle> customSortTitles, string cutomSortTitleName)
        {
            Objects = new List<SangoObject>(sangoObjects);
            customSortItems = customSortTitles;
            this.customSortTitleName = cutomSortTitleName;
            Push();
        }

        public void OnCancel()
        {
            Back();
        }

        /// <summary>
        /// 进入当前命令的时候触发
        /// </summary>
        public override void OnEnter()
        {
            Window.WindowInterface win = Window.Instance.Open(windowName);
            if (win != null)
            {
                UIObjectSelector uIObjectSelector = win.ugui_instance as UIObjectSelector;
                if (uIObjectSelector != null)
                {
                    uIObjectSelector.Init(this);
                }
            }
        }

        public override void OnDestroy()
        {
            Window.Instance.Close(windowName);
        }

        public override void OnBack(ICommandEvent whoGone)
        {
            Window.Instance.SetVisible(windowName, true);
            if(ClickMode)
            {
                Window.Instance.GetWindow(windowName).Refresh();
            }
        }

        public override void OnExit()
        {
            Window.Instance.SetVisible(windowName, false);
        }

        public virtual List<ObjectSortTitle> GetSortTitleGroup(int index)
        {
            return customSortItems;
        }

        public virtual string GetSortTitleGroupName(int index)
        {
            return "";
        }

        public override void HandleEvent(CommandEventType eventType, Cell cell, Vector3 clickPosition, bool isOverUI)
        {
            switch (eventType)
            {
                case CommandEventType.Cancel:
                case CommandEventType.RClick:
                    OnCancel(); break;
            }

        }
    }
}
