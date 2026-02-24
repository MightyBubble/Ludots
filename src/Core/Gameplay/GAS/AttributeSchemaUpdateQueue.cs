using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// ExtensionAttribute注册请求队列
    /// 存储待注册的ExtensionAttribute请求，在Phase 0统一处理
    /// </summary>
    public class AttributeSchemaUpdateQueue
    {
        private readonly List<string> _pendingRegistrations = new();
        
        /// <summary>
        /// 添加注册请求
        /// </summary>
        public void EnqueueRegistration(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return;
            }
            
            // 避免重复添加
            if (!_pendingRegistrations.Contains(fullName))
            {
                _pendingRegistrations.Add(fullName);
            }
        }
        
        /// <summary>
        /// 获取所有待注册的请求
        /// </summary>
        public IReadOnlyList<string> GetPendingRegistrations()
        {
            return _pendingRegistrations;
        }
        
        /// <summary>
        /// 清空队列
        /// </summary>
        public void Clear()
        {
            _pendingRegistrations.Clear();
        }
        
        /// <summary>
        /// 获取待注册数量
        /// </summary>
        public int PendingCount => _pendingRegistrations.Count;
    }
}
