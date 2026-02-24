namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// ExtensionAttribute缓冲区（固定容量，避免GC）
    /// 用于存储运行时注册的扩展属性
    /// </summary>
    public unsafe struct ExtensionAttributeBuffer
    {
        public const int CAPACITY = 50;
        
        public fixed int AttributeIds[CAPACITY];
        public fixed float BaseValues[CAPACITY];
        public fixed float CurrentValues[CAPACITY];
        public int Count;
        
        /// <summary>
        /// 尝试获取属性值
        /// </summary>
        public bool TryGetValue(int attrId, out float value)
        {
            if (attrId < 0)
            {
                value = 0;
                return false;
            }
            
            for (int i = 0; i < Count; i++)
            {
                if (AttributeIds[i] == attrId)
                {
                    value = CurrentValues[i];
                    return true;
                }
            }
            
            value = 0;
            return false;
        }
        
        /// <summary>
        /// 设置属性值
        /// </summary>
        public void SetValue(int attrId, float value)
        {
            if (attrId < 0)
            {
                return;
            }
            
            // 查找现有条目
            for (int i = 0; i < Count; i++)
            {
                if (AttributeIds[i] == attrId)
                {
                    CurrentValues[i] = value;
                    return;
                }
            }
            
            // 添加新条目
            if (Count < CAPACITY)
            {
                AttributeIds[Count] = attrId;
                BaseValues[Count] = value;
                CurrentValues[Count] = value;
                Count++;
            }
        }
        
        /// <summary>
        /// 设置Base值
        /// </summary>
        public void SetBaseValue(int attrId, float value)
        {
            if (attrId < 0)
            {
                return;
            }
            
            // 查找现有条目
            for (int i = 0; i < Count; i++)
            {
                if (AttributeIds[i] == attrId)
                {
                    BaseValues[i] = value;
                    return;
                }
            }
            
            // 添加新条目
            if (Count < CAPACITY)
            {
                AttributeIds[Count] = attrId;
                BaseValues[Count] = value;
                CurrentValues[Count] = value;
                Count++;
            }
        }
        
        /// <summary>
        /// 获取Base值
        /// </summary>
        public bool TryGetBaseValue(int attrId, out float value)
        {
            if (attrId < 0)
            {
                value = 0;
                return false;
            }
            
            for (int i = 0; i < Count; i++)
            {
                if (AttributeIds[i] == attrId)
                {
                    value = BaseValues[i];
                    return true;
                }
            }
            
            value = 0;
            return false;
        }
        
        /// <summary>
        /// 移除属性
        /// </summary>
        public bool RemoveAttribute(int attrId)
        {
            if (attrId < 0)
            {
                return false;
            }
            
            for (int i = 0; i < Count; i++)
            {
                if (AttributeIds[i] == attrId)
                {
                    // 移动最后一个到当前位置
                    AttributeIds[i] = AttributeIds[Count - 1];
                    BaseValues[i] = BaseValues[Count - 1];
                    CurrentValues[i] = CurrentValues[Count - 1];
                    Count--;
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// 检查是否有该属性
        /// </summary>
        public bool HasAttribute(int attrId)
        {
            if (attrId < 0)
            {
                return false;
            }
            
            for (int i = 0; i < Count; i++)
            {
                if (AttributeIds[i] == attrId)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// 清空所有属性
        /// </summary>
        public void Clear()
        {
            Count = 0;
        }
    }
}
