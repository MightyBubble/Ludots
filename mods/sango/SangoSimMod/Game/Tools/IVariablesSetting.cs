using System.Collections.Generic;
using UnityEngine;

namespace Sango.Core
{
    public interface IVariablesSetting
    {
        void RefreshSetting();
        void RemoveItem(GameObject item);
        GameObject AddBigTitle(string title);
        GameObject AddTitle(string title);
        GameObject AddNumberItem(string title, int number, int min, int max, System.Action<int> onChange);
        GameObject AddNumberItem(string title, float number, float min, float max, System.Action<float> onChange);
        GameObject AddSliderItem(string title, int value, int min, int max, System.Action<int> onValueChange);
        GameObject AddSliderItem(string title, float value, float min, float max, System.Action<float> onValueChange);
        GameObject AddToggleItem(string title, bool value, System.Action<bool> onValueChange) ;
        GameObject AddDropdownItem(string title, int value, List<string> values, System.Action<int> onValueChange);
        GameObject AddToggleGroupItem(string title, int value, List<string> values, System.Action<int> onValueChange);
        void SetItemBehindThis(GameObject item, GameObject t);


    }
}
