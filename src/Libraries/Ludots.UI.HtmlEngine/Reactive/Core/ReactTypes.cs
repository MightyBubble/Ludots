using System;
using System.Collections.Generic;

namespace Ludots.UI.Reactive.Core
{
    public class Element
    {
        public Type Type { get; }
        public object Props { get; }
        public List<Element> Children { get; } = new List<Element>();
        public string Key { get; }

        public Element(Type type, object props = null, string key = null, params Element[] children)
        {
            Type = type;
            Props = props;
            Key = key;
            if (children != null)
            {
                Children.AddRange(children);
            }
        }
    }

    public abstract class Component
    {
        public object Props { get; set; }
        
        // Injected by Reconciler
        internal Action<Component> RequestUpdate { get; set; }
        
        public abstract Element Render();

        protected void SetState(Action action)
        {
            action?.Invoke();
            RequestUpdate?.Invoke(this);
        }

        public virtual void OnMount() { }
        public virtual void OnUnmount() { }
    }
}
