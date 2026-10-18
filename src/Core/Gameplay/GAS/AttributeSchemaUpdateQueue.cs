using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// 存储待注册的ExtensionAttribute请求，在Phase 0统一处理
    /// </summary>
    public class AttributeSchemaUpdateQueue
    {
        private readonly List<string> _pendingRegistrations = new();
        
        public void EnqueueRegistration(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return;
            }
            
            if (!_pendingRegistrations.Contains(fullName))
            {
                _pendingRegistrations.Add(fullName);
            }
        }
        
        public IReadOnlyList<string> GetPendingRegistrations()
        {
            return _pendingRegistrations;
        }
        
        public void Clear()
        {
            _pendingRegistrations.Clear();
        }
        
        public int PendingCount => _pendingRegistrations.Count;
    }
}
