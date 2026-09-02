using System.Collections.Generic;

namespace Sango.Core.Tools
{
    public class OverrideData<T>
    {
        public T Value { get; set; }
        public T ValueAndRecycle
        {
            get
            {
                Recycle();
                return Value;
            }
        }
        public OverrideData(T baseValue) { Value = baseValue; }
        public OverrideData<T> Set(T baseValue) { Value = baseValue; return this; }

        static Queue<OverrideData<T>> pool_list = new Queue<OverrideData<T>>();

        public static OverrideData<T> Create(T value)
        {
            OverrideData<T> obj;
            if (pool_list.Count > 0)
            {
                obj = pool_list.Dequeue();
                obj.Set(value);
            }
            else
                obj = new OverrideData<T>(value);

            return obj;
        }

        public void Recycle()
        {
            if (pool_list.Contains(this))
            {
                Sango.Log.Error("重复!!!!!!");
                return;
            }
            pool_list.Enqueue(this);
        }
    }

}
