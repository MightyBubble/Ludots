using System;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Registry mapping <see cref="EffectPresetType"/> to <see cref="PresetTypeDefinition"/>.
    /// Fixed-size array indexed by byte-valued enum. Zero GC.
    /// Loaded from preset_types.json at startup, before EffectTemplateLoader runs.
    /// </summary>
    public sealed class PresetTypeRegistry
    {
        public const int MaxPresetTypes = 256;

        private readonly PresetTypeDefinition[] _definitions = new PresetTypeDefinition[MaxPresetTypes];
        private readonly bool[] _registered = new bool[MaxPresetTypes];

        /// <summary>Register a preset type definition.</summary>
        public void Register(in PresetTypeDefinition def)
        {
            int idx = (byte)def.Type;
            _definitions[idx] = def;
            _registered[idx] = true;
        }

        /// <summary>Get the definition for a preset type. Returns ref for zero-copy read.</summary>
        public ref readonly PresetTypeDefinition Get(EffectPresetType type)
        {
            return ref _definitions[(byte)type];
        }

        /// <summary>Try to get a definition. Returns false if not registered.</summary>
        public bool TryGet(EffectPresetType type, out PresetTypeDefinition def)
        {
            int idx = (byte)type;
            if (_registered[idx])
            {
                def = _definitions[idx];
                return true;
            }
            def = default;
            return false;
        }

        /// <summary>Check if a preset type is registered.</summary>
        public bool IsRegistered(EffectPresetType type)
        {
            return _registered[(byte)type];
        }

        /// <summary>Clear all registrations.</summary>
        public void Clear()
        {
            Array.Clear(_definitions, 0, MaxPresetTypes);
            Array.Clear(_registered, 0, MaxPresetTypes);
        }
    }
}
