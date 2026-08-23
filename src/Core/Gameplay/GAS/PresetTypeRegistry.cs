using System;
using Ludots.Core.Modding;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Registry mapping semantic preset keys to <see cref="PresetTypeDefinition"/>.
    /// Builtins keep their enum numeric ids; custom presets receive startup-assigned ids.
    /// </summary>
    public sealed class PresetTypeRegistry
    {
        public const int FirstModPresetTypeId = 1024;
        public const int MaxPresetTypes = 2048;
        public const string DuplicateRegistrationError = "GAS.PRESET_TYPE.ERR.DuplicateRegistration";

        private readonly PresetTypeDefinition[] _definitions = new PresetTypeDefinition[MaxPresetTypes];
        private readonly bool[] _registered = new bool[MaxPresetTypes];
        private readonly ExtensionKeyRegistry _keys = new(
            capacity: 128,
            firstDynamicId: FirstModPresetTypeId,
            maxIdExclusive: MaxPresetTypes,
            comparer: StringComparer.Ordinal);

        public PresetTypeRegistry()
        {
            foreach (EffectPresetType type in Enum.GetValues<EffectPresetType>())
            {
                if (type == EffectPresetType.None)
                {
                    continue;
                }

                _keys.RegisterFixed(type.ToString(), (byte)type);
            }
        }

        /// <summary>Register a preset type definition.</summary>
        public void Register(in PresetTypeDefinition def)
        {
            if (_keys.IsFrozen)
            {
                throw new InvalidOperationException("Preset type registry is frozen. Cannot register preset type definitions.");
            }

            var normalized = NormalizeDefinition(in def);
            int idx = normalized.TypeId;
            if ((uint)idx >= MaxPresetTypes)
            {
                throw new ArgumentOutOfRangeException(nameof(def), $"Preset type id {idx} exceeds MaxPresetTypes ({MaxPresetTypes}).");
            }

            if (_registered[idx])
            {
                throw new InvalidOperationException(
                    $"{DuplicateRegistrationError}: presetType={normalized.TypeKey} (id {idx}).");
            }

            if (!string.IsNullOrWhiteSpace(normalized.TypeKey))
            {
                if (idx >= FirstModPresetTypeId)
                {
                    int allocated = _keys.RegisterDynamic(normalized.TypeKey);
                    if (allocated != idx)
                    {
                        throw new InvalidOperationException(
                            $"Preset type '{normalized.TypeKey}' resolved id {allocated}, but definition declares {idx}.");
                    }
                }
                else
                {
                    _keys.RegisterFixed(normalized.TypeKey, idx);
                }
            }

            _definitions[idx] = normalized;
            _registered[idx] = true;
        }

        public int RegisterKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Preset type key must not be empty.", nameof(key));
            }

            if (Enum.TryParse(key, ignoreCase: false, out EffectPresetType builtin) &&
                builtin != EffectPresetType.None &&
                Enum.IsDefined(typeof(EffectPresetType), builtin))
            {
                return (byte)builtin;
            }

            return _keys.RegisterDynamic(key);
        }

        public bool TryGetId(string key, out int typeId)
        {
            if (Enum.TryParse(key, ignoreCase: false, out EffectPresetType builtin) &&
                builtin != EffectPresetType.None &&
                Enum.IsDefined(typeof(EffectPresetType), builtin))
            {
                typeId = (byte)builtin;
                return true;
            }

            return _keys.TryGetId(key, out typeId);
        }

        public int GetId(string key)
        {
            return TryGetId(key, out int typeId) ? typeId : 0;
        }

        public string GetKey(int typeId)
        {
            return _keys.GetKey(typeId);
        }

        /// <summary>Get the definition for a preset type. Returns ref for zero-copy read.</summary>
        public ref readonly PresetTypeDefinition Get(EffectPresetType type)
        {
            return ref _definitions[(byte)type];
        }

        public ref readonly PresetTypeDefinition Get(int typeId)
        {
            return ref _definitions[typeId];
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

        public bool TryGet(int typeId, out PresetTypeDefinition def)
        {
            if ((uint)typeId < MaxPresetTypes && _registered[typeId])
            {
                def = _definitions[typeId];
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

        public bool IsRegistered(int typeId)
        {
            return (uint)typeId < MaxPresetTypes && _registered[typeId];
        }

        public void Freeze()
        {
            _keys.Freeze();
        }

        /// <summary>Clear all registrations.</summary>
        public void Clear()
        {
            Array.Clear(_definitions, 0, MaxPresetTypes);
            Array.Clear(_registered, 0, MaxPresetTypes);
            _keys.Clear();
            foreach (EffectPresetType type in Enum.GetValues<EffectPresetType>())
            {
                if (type != EffectPresetType.None)
                {
                    _keys.RegisterFixed(type.ToString(), (byte)type);
                }
            }
        }

        private PresetTypeDefinition NormalizeDefinition(in PresetTypeDefinition def)
        {
            var normalized = def;
            if (normalized.TypeId == 0 && normalized.Type != EffectPresetType.None)
            {
                normalized.TypeId = (byte)normalized.Type;
            }

            if (normalized.TypeId == 0 && normalized.Type != EffectPresetType.None)
            {
                throw new InvalidOperationException("PresetTypeDefinition.TypeId must be assigned.");
            }

            if (string.IsNullOrWhiteSpace(normalized.TypeKey))
            {
                normalized.TypeKey = normalized.Type != EffectPresetType.None
                    ? normalized.Type.ToString()
                    : GetKey(normalized.TypeId);
            }

            if (normalized.Type != EffectPresetType.None && normalized.TypeId != (byte)normalized.Type)
            {
                throw new InvalidOperationException(
                    $"PresetTypeDefinition '{normalized.TypeKey}' has mismatched Type={normalized.Type} and TypeId={normalized.TypeId}.");
            }

            return normalized;
        }
    }
}
