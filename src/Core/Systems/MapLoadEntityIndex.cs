using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Systems
{
    public sealed class MapLoadEntityIndex
    {
        private readonly Dictionary<string, Entity> _byInstanceId = new(StringComparer.Ordinal);
        private readonly Dictionary<Entity, string> _instanceIdByEntity = new();
        private readonly Dictionary<string, Entity> _byLocalPath = new(StringComparer.Ordinal);

        public int Count => _byInstanceId.Count;

        public IReadOnlyDictionary<string, Entity> ByInstanceId => _byInstanceId;

        /// <summary>
        /// 可寻址路径节点数（实例根 + 有 localId 的后代）。切A 只登记形状；
        /// 组件相对引用的解析与统一命名空间升格属切F/切D。
        /// </summary>
        public int LocalPathCount => _byLocalPath.Count;

        public bool TryGetByLocalPath(string localPath, out Entity entity)
        {
            entity = Entity.Null;
            if (string.IsNullOrWhiteSpace(localPath))
            {
                return false;
            }

            return _byLocalPath.TryGetValue(localPath, out entity);
        }

        public void Register(string mapId, string instanceId, Entity entity)
        {
            if (instanceId == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity InstanceId requires a non-empty value when authored.");
            }

            string normalized = instanceId.Trim();
            if (!string.Equals(instanceId, normalized, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity InstanceId '{instanceId}' must be trimmed.");
            }

            if (_byInstanceId.ContainsKey(normalized))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' contains duplicate entity InstanceId '{normalized}'.");
            }

            _byInstanceId.Add(normalized, entity);
            _instanceIdByEntity[entity] = normalized;
        }

        /// <summary>Reverse lookup for exact-instance event filtering: the placed
        /// InstanceId an entity was registered under, if any.</summary>
        public bool TryGetInstanceId(Entity entity, out string instanceId)
        {
            return _instanceIdByEntity.TryGetValue(entity, out instanceId!);
        }

        public bool TryGet(string instanceId, out Entity entity)
        {
            entity = Entity.Null;
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return false;
            }

            return _byInstanceId.TryGetValue(instanceId, out entity);
        }

        public Entity GetRequired(string mapId, string instanceId, string context)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' {context} requires a non-empty RepresentativeInstanceId.");
            }

            if (!_byInstanceId.TryGetValue(instanceId, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' {context} references unresolved entity InstanceId '{instanceId}'.");
            }

            return entity;
        }

        /// <summary>
        /// 登记一个可寻址路径节点（切A：装载期构建"本地路径 → 实体"映射，每地图一张）。
        /// 路径已由调用方按实例根实例化；重复路径 fail-fast。
        /// </summary>
        public void RegisterLocalPath(string mapId, string localPath, Entity entity)
        {
            if (string.IsNullOrWhiteSpace(localPath))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' local path node requires a non-empty path.");
            }

            if (_byLocalPath.ContainsKey(localPath))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' contains duplicate addressable local path '{localPath}'.");
            }

            _byLocalPath.Add(localPath, entity);
        }
    }
}
