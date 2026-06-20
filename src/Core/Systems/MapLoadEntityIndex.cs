using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Systems
{
    public sealed class MapLoadEntityIndex
    {
        private readonly Dictionary<string, Entity> _byInstanceId = new(StringComparer.Ordinal);

        public int Count => _byInstanceId.Count;

        public IReadOnlyDictionary<string, Entity> ByInstanceId => _byInstanceId;

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
    }
}
