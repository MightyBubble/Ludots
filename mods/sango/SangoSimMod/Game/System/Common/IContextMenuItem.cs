using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sango.Core
{
    public interface IContextMenuItem 
    {
        public object CustomData { get; set; }
        public void SetTitle(string t);
    }
}
