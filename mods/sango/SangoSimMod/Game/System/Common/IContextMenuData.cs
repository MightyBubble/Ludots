using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sango.Core
{
    public interface IContextMenuData
    {
        /// <summary>
        /// order越小越靠前,菜单链父级菜单以最小子级菜单order计算
        /// </summary>
        /// <param name="title"></param>
        /// <param name="order"></param>
        /// <param name="custom"></param>
        /// <param name="action"></param>
        public void Add(string title, int order, object custom, Action<IContextMenuItem> action, bool valide = true);
        public void Add(string title, int order, object custom);
        public void Add(string title, int order);
        public void Add(string title);
        public void AddLine();
        public void Clear();
        public bool IsEmpty();
    }
}
