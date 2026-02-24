using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Type tag for config parameter values.
    /// </summary>
    public enum ConfigParamType : byte
    {
        Float = 0,
        Int = 1,
        EffectTemplateId = 2,
        /// <summary>Attribute name resolved to AttributeId at load time. Stored as int.</summary>
        AttributeId = 3,
    }

    /// <summary>
    /// Per-EffectTemplate parameter table that Graph programs read via LoadConfig* nodes.
    /// Enables a single Graph program to be reused across multiple EffectTemplates with
    /// different parameterization (e.g. different child effect IDs, multipliers, caps).
    /// Zero-GC: fixed-size struct, no heap allocations.
    /// </summary>
    /// <summary>
    /// Per-EffectTemplate parameter table. Optimized: uses a single Values array
    /// (int/float union via Unsafe.As) instead of parallel IntValues+FloatValues,
    /// saving ~128 bytes per instance (292B vs 420B).
    /// </summary>
    public unsafe struct EffectConfigParams
    {
        public const int MAX_PARAMS = GasConstants.EFFECT_CONFIG_PARAMS_MAX;

        public int Count;
        public fixed int Keys[MAX_PARAMS];           // config key id (resolved from string at load time)
        public fixed byte Types[MAX_PARAMS];         // ConfigParamType
        public fixed int Values[MAX_PARAMS];         // int, effectTemplateId, or float (via Unsafe.As)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetFloat(int keyId, out float value)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Keys[i] == keyId)
                {
                    value = Unsafe.As<int, float>(ref Values[i]);
                    return true;
                }
            }
            value = 0f;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetInt(int keyId, out int value)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Keys[i] == keyId)
                {
                    value = Values[i];
                    return true;
                }
            }
            value = 0;
            return false;
        }

        /// <summary>Add a float parameter. Returns false if capacity exceeded.</summary>
        public bool TryAddFloat(int keyId, float value)
        {
            if (Count >= MAX_PARAMS) return false;
            Keys[Count] = keyId;
            Types[Count] = (byte)ConfigParamType.Float;
            Unsafe.As<int, float>(ref Values[Count]) = value;
            Count++;
            return true;
        }

        /// <summary>Add an int parameter. Returns false if capacity exceeded.</summary>
        public bool TryAddInt(int keyId, int value)
        {
            if (Count >= MAX_PARAMS) return false;
            Keys[Count] = keyId;
            Types[Count] = (byte)ConfigParamType.Int;
            Values[Count] = value;
            Count++;
            return true;
        }

        /// <summary>Get an attribute ID parameter (resolved from attribute name at load time).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetAttributeId(int keyId, out int attributeId)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Keys[i] == keyId)
                {
                    attributeId = Values[i];
                    return true;
                }
            }
            attributeId = 0;
            return false;
        }

        /// <summary>Add an attribute ID parameter (resolved from attribute name at load time). Returns false if capacity exceeded.</summary>
        public bool TryAddAttributeId(int keyId, int attributeId)
        {
            if (Count >= MAX_PARAMS) return false;
            Keys[Count] = keyId;
            Types[Count] = (byte)ConfigParamType.AttributeId;
            Values[Count] = attributeId;
            Count++;
            return true;
        }

        /// <summary>Add an effect template ID parameter. Returns false if capacity exceeded.</summary>
        public bool TryAddEffectTemplateId(int keyId, int templateId)
        {
            if (Count >= MAX_PARAMS) return false;
            Keys[Count] = keyId;
            Types[Count] = (byte)ConfigParamType.EffectTemplateId;
            Values[Count] = templateId;
            Count++;
            return true;
        }

        /// <summary>
        /// Merge caller-supplied params into this instance. Caller wins on key conflict.
        /// Used to overlay CallerParams (from EffectRequest or AbilityTimeline)
        /// over template-defined ConfigParams.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MergeFrom(in EffectConfigParams caller)
        {
            for (int ci = 0; ci < caller.Count; ci++)
            {
                int callerKey = caller.Keys[ci];

                // Try to find existing entry with same key and overwrite
                bool found = false;
                for (int ti = 0; ti < Count; ti++)
                {
                    if (Keys[ti] == callerKey)
                    {
                        Types[ti] = caller.Types[ci];
                        Values[ti] = caller.Values[ci];
                        found = true;
                        break;
                    }
                }

                // Not found → append if capacity allows
                if (!found && Count < MAX_PARAMS)
                {
                    Keys[Count] = callerKey;
                    Types[Count] = caller.Types[ci];
                    Values[Count] = caller.Values[ci];
                    Count++;
                }
            }
        }
    }
}
