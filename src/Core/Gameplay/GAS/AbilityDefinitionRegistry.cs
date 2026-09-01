using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Orders;

namespace Ludots.Core.Gameplay.GAS
{
    public struct AbilityInputBindingOverride
    {
        public bool HasTrigger;
        public InputTriggerType Trigger;
        public bool HasHeldPolicy;
        public HeldPolicy HeldPolicy;
        public bool HasCastModeOverride;
        public CastModeType CastModeOverride;
        public bool HasAutoTargetPolicy;
        public AutoTargetPolicy AutoTargetPolicy;
        public bool HasAutoTargetRangeCm;
        public int AutoTargetRangeCm;
    }

    public sealed class AbilityPresentationConfig
    {
        public string DisplayName { get; init; } = string.Empty;
        public string IconGlyph { get; init; } = string.Empty;
        public string AccentColorHex { get; init; } = string.Empty;
        public string HintText { get; init; } = string.Empty;

        /// <summary>
        /// PresentationText token key for the ability display name. Preferred over <see cref="DisplayName"/>
        /// for tooltip / WebUI projection. When set, loaders must validate token + locale coverage.
        /// </summary>
        public string DisplayNameToken { get; init; } = string.Empty;

        /// <summary>
        /// PresentationText token key for the ability hint. Preferred over <see cref="HintText"/>.
        /// </summary>
        public string HintTextToken { get; init; } = string.Empty;

        public Dictionary<string, string> ModeIconGlyphOverrides { get; } = new(System.StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ModeHintOverrides { get; } = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Interaction-mode hint token keys (mode name → PresentationText token). Preferred over
        /// <see cref="ModeHintOverrides"/> final strings for tooltip / WebUI projection.
        /// </summary>
        public Dictionary<string, string> ModeHintTokenOverrides { get; } = new(System.StringComparer.OrdinalIgnoreCase);

        public bool HasPresentationTokens =>
            !string.IsNullOrWhiteSpace(DisplayNameToken) ||
            !string.IsNullOrWhiteSpace(HintTextToken) ||
            ModeHintTokenOverrides.Count > 0;

        public string ResolveDisplayName(string fallback)
        {
            // Legacy final-string path. Tooltip / WebUI must use DisplayNameToken via
            // AbilityPresentationTextValidator instead of this fallback.
            return string.IsNullOrWhiteSpace(DisplayName) ? fallback : DisplayName;
        }

        public string ResolveIconGlyph(string? interactionMode, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(interactionMode) &&
                ModeIconGlyphOverrides.TryGetValue(interactionMode, out string? overrideGlyph) &&
                !string.IsNullOrWhiteSpace(overrideGlyph))
            {
                return overrideGlyph;
            }

            return string.IsNullOrWhiteSpace(IconGlyph) ? fallback : IconGlyph;
        }

        public string ResolveHintText(string? interactionMode, string fallback)
        {
            // Legacy final-string path. Tooltip / WebUI must use HintTextToken /
            // ModeHintTokenOverrides via AbilityPresentationTextValidator.
            if (!string.IsNullOrWhiteSpace(interactionMode) &&
                ModeHintOverrides.TryGetValue(interactionMode, out string? overrideHint) &&
                !string.IsNullOrWhiteSpace(overrideHint))
            {
                return overrideHint;
            }

            return string.IsNullOrWhiteSpace(HintText) ? fallback : HintText;
        }
    }

    public struct AbilityTargetingConfig
    {
        public float CastRangeCm;
        public int ImpactEffectTemplateId;
    }

    /// <summary>
    /// Toggle ability specification. When an ability has this spec, pressing the same key
    /// alternates between activate and deactivate. The toggle state is tracked by a tag.
    /// </summary>
    public struct AbilityToggleSpec
    {
        /// <summary>Tag ID used to track toggle state. If present on actor 鈫?ability is ON.</summary>
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
        /// <summary>Ordered TriggerGraph names owned by this ability definition.</summary>
        public List<string> TriggerGraphs;
        // 鈹€鈹€ Generic execution model 鈹€鈹€
        public AbilityExecSpec ExecSpec;
        public AbilityExecCallerParamsPool ExecCallerParamsPool;
        public bool HasExecCallerParamsPool;

        public AbilityOnActivateEffects OnActivateEffects;
        public bool HasOnActivateEffects;
        public AbilityActivationBlockTags ActivationBlockTags;
        public bool HasActivationBlockTags;
        public AbilityActivationPrecondition ActivationPrecondition;
        public bool HasActivationPrecondition;

        // 鈹€鈹€ Toggle mode 鈹€鈹€
        public bool HasToggleSpec;
        public AbilityToggleSpec ToggleSpec;

        // 鈹€鈹€ Presentation metadata 鈹€鈹€
        public bool HasTargeting;
        public AbilityTargetingConfig Targeting;
        public bool HasPresentation;
        public AbilityPresentationConfig? Presentation;
        public bool HasInputBindingOverride;
        public AbilityInputBindingOverride InputBindingOverride;

        public int UseProgressionRequirementId;
        public bool HasUseProgressionRequirement;
        public int ShowProgressionRequirementId;
        public bool HasShowProgressionRequirement;

        // ── Category classification (RFC-0065 DEC-14) ──
        /// <summary>
        /// Categories declared on the ability (abilities.json <c>categories</c>).
        /// Classification for semantic routing (CommandIntentProfile / aggregation);
        /// ids come from <see cref="Registry.AbilityCategoryRegistry"/>, not TagRegistry.
        /// </summary>
        public GameplayTagContainer Categories;
        public bool HasCategories;

        // ── Interaction context binding (RFC-0065 CTX-6) ──
        /// <summary>
        /// Optional InteractionContextProfile id (abilities.json <c>interactionContextProfile</c>).
        /// While an exec instance of this ability runs, the client pushes the profile's context
        /// frame and reclaims it when the exec ends/aborts; Core never interprets the id.
        /// </summary>
        public string InteractionContextProfileId;
        public bool HasInteractionContextProfile;
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

        public void Clear()
        {
            System.Array.Clear(_items, 0, _items.Length);
            System.Array.Clear(_has, 0, _has.Length);
            _registrationSource.Clear();
            _registeredIds = null;
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
                Log.Warn(in LogChannels.GAS, $"AbilityId {abilityId} registered by '{existingMod}', overwritten by '{newMod}' (last-wins).");
                _conflictReport?.Add("AbilityDefinitionRegistry", abilityId.ToString(), existingMod, newMod);
            }
#endif
            _items[abilityId] = definition;
            _has[abilityId] = true;
            _registrationSource[abilityId] = modId ?? "(core)";
            _registeredIds = null;
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

        private List<int>? _registeredIds;

        /// <summary>
        /// Ascending snapshot of registered ability ids. Cached and invalidated on
        /// <see cref="Register"/>/<see cref="Clear"/>; the returned list is a stable snapshot
        /// and never refreshes in place, so callers must re-read after a later registration.
        /// </summary>
        public IReadOnlyList<int> RegisteredAbilityIds
        {
            get
            {
                List<int>? ids = _registeredIds;
                if (ids == null)
                {
                    ids = new List<int>();
                    for (int i = 1; i < _has.Length; i++)
                    {
                        if (_has[i]) ids.Add(i);
                    }

                    _registeredIds = ids;
                }

                return ids;
            }
        }

        /// <summary>In-place category probe (no definition copy); hot path for semantic routing (RFC-0065 DEC-14).</summary>
        public bool HasCategory(int abilityId, int categoryId)
        {
            if (abilityId <= 0 || abilityId >= _items.Length || !_has[abilityId])
            {
                return false;
            }

            ref readonly AbilityDefinition definition = ref _items[abilityId];
            return definition.HasCategories && definition.Categories.HasTag(categoryId);
        }

        /// <summary>
        /// In-place interaction context profile probe (no definition copy); hot path for the exec
        /// lifecycle context system (RFC-0065 CTX-6). False when the ability is unknown or declares
        /// no <see cref="AbilityDefinition.InteractionContextProfileId"/>.
        /// </summary>
        public bool TryGetInteractionContextProfileId(int abilityId, out string profileId)
        {
            if (abilityId <= 0 || abilityId >= _items.Length || !_has[abilityId])
            {
                profileId = string.Empty;
                return false;
            }

            ref readonly AbilityDefinition definition = ref _items[abilityId];
            if (!definition.HasInteractionContextProfile)
            {
                profileId = string.Empty;
                return false;
            }

            profileId = definition.InteractionContextProfileId;
            return true;
        }

        /// <summary>
        /// Lowest category id shared between the ability's <see cref="AbilityDefinition.Categories"/>
        /// and <paramref name="mask"/>; 0 when the ability is unknown, has no categories, or none match.
        /// In-place probe (no definition copy); hot path for panel aggregation (RFC-0065 DEC-10).
        /// </summary>
        public int FirstCategoryIntersection(int abilityId, in GameplayTagContainer mask)
        {
            if (abilityId <= 0 || abilityId >= _items.Length || !_has[abilityId])
            {
                return 0;
            }

            ref readonly AbilityDefinition definition = ref _items[abilityId];
            return definition.HasCategories ? definition.Categories.FirstCommonTag(in mask) : 0;
        }

        public void RegisterFromEntity(World world, Entity templateEntity, int abilityId)
        {
            if (!world.IsAlive(templateEntity) || abilityId <= 0) return;
            if (!world.Has<AbilityExecSpec>(templateEntity)) return;

            var def = new AbilityDefinition
            {
                HasOnActivateEffects = world.Has<AbilityOnActivateEffects>(templateEntity),
                HasActivationBlockTags = world.Has<AbilityActivationBlockTags>(templateEntity),
                HasActivationPrecondition = world.Has<AbilityActivationPrecondition>(templateEntity),
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
            if (def.HasActivationPrecondition)
            {
                def.ActivationPrecondition = world.Get<AbilityActivationPrecondition>(templateEntity);
            }
            if (world.Has<AbilityProgressionRequirements>(templateEntity))
            {
                var requirements = world.Get<AbilityProgressionRequirements>(templateEntity);
                if (requirements.UseRequirementId > 0)
                {
                    def.UseProgressionRequirementId = requirements.UseRequirementId;
                    def.HasUseProgressionRequirement = true;
                }

                if (requirements.ShowRequirementId > 0)
                {
                    def.ShowProgressionRequirementId = requirements.ShowRequirementId;
                    def.HasShowProgressionRequirement = true;
                }
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
