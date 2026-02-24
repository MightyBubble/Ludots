using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// ExtensionAttribute注册表（运行时注册）
    /// 根据裁决AG：ID范围10001-20000为运行时重映射区
    /// </summary>
    public class ExtensionAttributeRegistry
    {
        private const int MAX_EXTENSION_ATTRS = 1000;
        
        // FullName -> AttributeId映射
        private readonly Dictionary<string, int> _nameToId = new();
        private readonly Dictionary<int, string> _idToName = new();
        
        // 下一个可用的AttributeId（从10001开始，见裁决AG）
        private int _nextId = 10001;
        
        /// <summary>
        /// 注册ExtensionAttribute
        /// </summary>
        /// <param name="fullName">属性的完整名称（例如："Mod.MyMod.Attributes.CustomAttr"）</param>
        /// <returns>分配的AttributeId</returns>
        public int Register(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                throw new ArgumentException("FullName cannot be null or empty", nameof(fullName));
            }
            
            // 检查是否已注册
            if (_nameToId.TryGetValue(fullName, out var existingId))
            {
                return existingId;
            }
            
            // 检查ID空间是否耗尽
            if (_nextId > 20000) // 超过运行时重映射区上限
            {
                throw new InvalidOperationException($"ExtensionAttribute ID space exhausted (nextId: {_nextId}, max: 20000)");
            }
            
            // 分配新ID
            var id = _nextId++;
            _nameToId[fullName] = id;
            _idToName[id] = fullName;
            
            return id;
        }
        
        /// <summary>
        /// 尝试获取AttributeId
        /// </summary>
        public bool TryGetId(string fullName, out int id)
        {
            return _nameToId.TryGetValue(fullName, out id);
        }
        
        /// <summary>
        /// 尝试获取FullName
        /// </summary>
        public bool TryGetName(int id, out string name)
        {
            return _idToName.TryGetValue(id, out name);
        }
        
        /// <summary>
        /// 获取已注册的ExtensionAttribute数量
        /// </summary>
        public int RegisteredCount => _nameToId.Count;
        
        /// <summary>
        /// 获取下一个可用的ID
        /// </summary>
        public int NextAvailableId => _nextId;
    }
}
