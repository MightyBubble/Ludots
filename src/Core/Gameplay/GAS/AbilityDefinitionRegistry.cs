using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Visual indicator configuration for an ability (range circles, cones, etc.).
    /// </summary>
    public struct AbilityIndicatorConfig
    {
        public TargetShape Shape;
        public float Range;              // cast range (centimeters)
        public float Radius;             // AOE radius (centimeters)
        public float Angle;              // cone half-angle (radians)
        public Vector4 ValidColor;       // color when target is valid
        public Vector4 InvalidColor;     // color when out of range / invalid
        public Vector4 RangeCircleColor; // range circle fill color
        public bool ShowRangeCircle;     // whether to show the cast range circle
    }

    /// <summary>
    /// Toggle ability specification. When an ability has this spec, pressing the same key
    /// alternates between activate and deactivate. The toggle state is tracked by a tag.
    /// </summary>
    public struct AbilityToggleSpec
    {
        /// <summary>Tag ID used to track toggle state. If present on actor → ability is ON.</summary>
        public int ToggleTagId;
        
        /// <summary>
        /// Deactivate timeline (optional). If ItemCount == 0, deactivation is instantaneous.
        /// The activate path uses the regular ExecSpec.
        /// </summary>
        public AbilityExecSpec DeactivateExecSpec;
        
        /// <summary>
        /// Effect template IDs applied as infinite-duration effects while the toggle is ON.
        /// These are removed when deactivating. Up to 4 effects.
        /// </summary>
        public unsafe fixed int ActiveEffectTemplateIds[4];
        
        /// <summary>Number of active effect template IDs (0-4).</summary>
        public int ActiveEffectCount;
    }

    public struct AbilityDefinition
    {
        // ── Generic execution model ──
        public AbilityExecSpec ExecSpec;
        public AbilityExecCallerParamsPool ExecCallerParamsPool;
        public bool HasExecCallerParamsPool;

        public AbilityOnActivateEffects OnActivateEffects;
        public bool HasOnActivateEffects;
        public AbilityActivationBlockTags ActivationBlockTags;
        public bool HasActivationBlockTags;

        // ── Toggle mode ──
        public bool HasToggleSpec;
        public AbilityToggleSpec ToggleSpec;

        // ── Presentation metadata ──
        public bool HasIndicator;
        public AbilityIndicatorConfig Indicator;
    }

    public sealed class AbilityDefinitionRegistry
    {
        private AbilityDefinition[] _items = new AbilityDefinition[4096];
        private bool[] _has = new bool[4096];
        private readonly System.Collections.Generic.Dictionary<int, string> _registrationSource = new();
        private Ludots.Core.Modding.RegistrationConflictReport _conflictReport;

        public void SetConflictReport(Ludots.Core.Modding.RegistrationConflictReport report)
        {
            _conflictReport = report;
        }

        public void Register(int abilityId, in AbilityDefinition definition, string modId = null)
        {
            if (abilityId <= 0) return;
            EnsureCapacity(abilityId + 1);
#if DEBUG
            if (_has[abilityId])
            {
                string existingMod = _registrationSource.TryGetValue(abilityId, out var em) ? em : "(core)";
                string newMod = modId ?? "(core)";
                System.Console.WriteLine($"[AbilityDefinitionRegistry] WARNING: AbilityId {abilityId} registered by '{existingMod}', overwritten by '{newMod}' (last-wins).");
                _conflictReport?.Add("AbilityDefinitionRegistry", abilityId.ToString(), existingMod, newMod);
            }
#endif
            _items[abilityId] = definition;
            _has[abilityId] = true;
            _registrationSource[abilityId] = modId ?? "(core)";
        }

        public bool TryGet(int abilityId, out AbilityDefinition definition)
        {
            if (abilityId <= 0 || abilityId >= _items.Length || !_has[abilityId])
            {
                definition = default;
                return false;
            }

            definition = _items[abilityId];
            return true;
        }

        public void RegisterFromEntity(World world, Entity templateEntity, int abilityId)
        {
            if (!world.IsAlive(templateEntity) || abilityId <= 0) return;
            if (!world.Has<AbilityExecSpec>(templateEntity)) return;

            var def = new AbilityDefinition
            {
                HasOnActivateEffects = world.Has<AbilityOnActivateEffects>(templateEntity),
                HasActivationBlockTags = world.Has<AbilityActivationBlockTags>(templateEntity),
                ExecSpec = world.Get<AbilityExecSpec>(templateEntity)
            };

            if (world.Has<AbilityExecCallerParamsPool>(templateEntity))
            {
                def.ExecCallerParamsPool = world.Get<AbilityExecCallerParamsPool>(templateEntity);
                def.HasExecCallerParamsPool = true;
            }

            if (def.HasOnActivateEffects)
            {
                def.OnActivateEffects = world.Get<AbilityOnActivateEffects>(templateEntity);
            }
            if (def.HasActivationBlockTags)
            {
                def.ActivationBlockTags = world.Get<AbilityActivationBlockTags>(templateEntity);
            }
            Register(abilityId, in def);
        }

        private void EnsureCapacity(int capacity)
        {
            if (capacity <= _items.Length) return;
            int newCap = _items.Length;
            while (newCap < capacity) newCap *= 2;

            var newItems = new AbilityDefinition[newCap];
            var newHas = new bool[newCap];
            System.Array.Copy(_items, newItems, _items.Length);
            System.Array.Copy(_has, newHas, _has.Length);
            _items = newItems;
            _has = newHas;
        }
    }
}
