using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Registry of camera presets loaded from ConfigPipeline.
    /// </summary>
    public sealed class CameraPresetRegistry
    {
        private readonly Dictionary<string, CameraPreset> _presets = new(StringComparer.OrdinalIgnoreCase);

        public void Register(CameraPreset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Id))
                return;
            _presets[preset.Id] = preset;
        }

        public CameraPreset Get(string id)
        {
            return _presets.TryGetValue(id ?? "", out var p) ? p : null;
        }

        public bool TryGet(string id, out CameraPreset preset)
        {
            return _presets.TryGetValue(id ?? "", out preset);
        }
    }
}
