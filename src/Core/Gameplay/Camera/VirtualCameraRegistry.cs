using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Camera
{
    public sealed class VirtualCameraRegistry
    {
        private readonly Dictionary<string, VirtualCameraDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

        public void Register(VirtualCameraDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Id)) throw new InvalidOperationException("VirtualCameraDefinition.Id is required.");
            _definitions[definition.Id] = definition;
        }

        public bool TryGet(string id, out VirtualCameraDefinition definition)
        {
            return _definitions.TryGetValue(id, out definition!);
        }

        public VirtualCameraDefinition Get(string id)
        {
            if (!_definitions.TryGetValue(id, out var definition))
            {
                throw new InvalidOperationException($"Virtual camera '{id}' is not registered.");
            }

            return definition;
        }
    }
}
