using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Registry;

namespace Ludots.Core.UI.EntityCommandPanels
{
    /// <summary>
    /// AggregationProfile registry and evaluator (RFC-0065 PNL-2/3, DEC-10). Profiles are declared in
    /// <c>UI/ability_aggregation_profiles.json</c>; <c>groupBy</c> expressions compile at install time
    /// through a prefix-keyed selector delegate table (DEC-11: extensible via
    /// <see cref="RegisterKeySelectorPrefix"/>, never a closed enum). Built-in prefixes:
    /// <list type="bullet">
    /// <item><c>catalog.&lt;tagPrefix&gt;</c> — group by the first (lowest-id) ability catalog tag whose
    /// registered name starts with <c>&lt;tagPrefix&gt;.</c>; abilities without a matching tag fall back
    /// to their identity key (own group). The prefix mask is resolved against <see cref="TagRegistry"/>
    /// at install, so profiles must install after ability catalog tags are registered.</item>
    /// <item><c>template.id</c> / <c>ability.id</c> — in this repository ability ids are definition ids
    /// (<see cref="AbilityIdRegistry"/> is the single id space; there is no separate instantiated-ability
    /// id). <c>template.id</c> groups by owner unit template plus slot index, while
    /// <c>ability.id</c> groups by ability definition id.</item>
    /// </list>
    /// Unknown prefixes fail fast at install. Steady-state evaluation is allocation free.
    /// </summary>
    public sealed class AbilityAggregationProfileRegistry
    {
        private readonly StringIntRegistry _profileIds;
        private readonly Dictionary<string, AbilityAggregationKeySelectorFactory> _selectorFactoriesByPrefix =
            new(StringComparer.Ordinal);

        private CompiledProfile[] _profiles = new CompiledProfile[8];

        public AbilityAggregationProfileRegistry(StringIntRegistry profileIdRegistry)
        {
            _profileIds = profileIdRegistry ?? throw new ArgumentNullException(nameof(profileIdRegistry));
            _selectorFactoriesByPrefix.Add("catalog", CompileCatalogSelector);
            _selectorFactoriesByPrefix.Add("template", CompileTemplateSelector);
            _selectorFactoriesByPrefix.Add("ability", CompileAbilitySelector);
        }

        /// <summary>Profile id space; panel routers reference aggregation profiles by these ids.</summary>
        public StringIntRegistry ProfileIdRegistry => _profileIds;

        /// <summary>
        /// Register an additional <c>groupBy</c> prefix (DEC-11 registry extension point). The factory
        /// receives the expression remainder after <c>&lt;prefix&gt;.</c> and must fail fast on invalid
        /// arguments. Built-in prefixes cannot be re-registered.
        /// </summary>
        public void RegisterKeySelectorPrefix(string prefix, AbilityAggregationKeySelectorFactory factory)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException("Key selector prefix is required.", nameof(prefix));
            }

            prefix = prefix.Trim();
            if (_selectorFactoriesByPrefix.ContainsKey(prefix))
            {
                throw new InvalidOperationException($"Aggregation groupBy prefix '{prefix}' is already registered.");
            }

            _selectorFactoriesByPrefix.Add(prefix, factory ?? throw new ArgumentNullException(nameof(factory)));
        }

        /// <summary>
        /// Compile and install every profile in the config. Fails fast on unknown groupBy prefixes,
        /// invalid selector arguments, and duplicate installs.
        /// </summary>
        public void Install(AbilityAggregationProfilesConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            AbilityAggregationProfileConfigLoader.Validate(config, nameof(AbilityAggregationProfilesConfig));
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                InstallProfile(config.Profiles[i]);
            }
        }

        /// <summary>True when the profile id has been compiled and can be evaluated.</summary>
        public bool IsInstalled(int profileId)
        {
            return profileId > 0 && profileId < _profiles.Length && _profiles[profileId] != null;
        }

        /// <summary>Opaque overflow policy key of the profile for the panel router (PNL-3); empty when unset.</summary>
        public string GetOverflow(int profileId)
        {
            return RequireInstalled(profileId).Overflow;
        }

        /// <summary>
        /// Aggregate the effective ability slots of a multi-selection into panel groups. Every member's
        /// non-empty slots (four-layer <see cref="AbilitySlotResolver"/> merge: granted &gt; item &gt;
        /// form &gt; base) are keyed by the profile's groupBy selector and grouped into
        /// <paramref name="result"/> (reused pooled SoA; steady-state 0 alloc). Groups are ordered by
        /// key ascending, members by entity id then slot index ascending (deterministic). Members
        /// without an <see cref="AbilityStateBuffer"/> contribute no slots. Returns the group count;
        /// an empty selection yields 0 groups.
        /// </summary>
        public int BuildGroups(
            int profileId,
            ReadOnlySpan<Entity> members,
            World world,
            AbilityDefinitionRegistry abilities,
            ref AbilityAggregationResult result)
        {
            CompiledProfile profile = RequireInstalled(profileId);
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (abilities == null)
            {
                throw new ArgumentNullException(nameof(abilities));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            result.Reset();
            for (int memberIndex = 0; memberIndex < members.Length; memberIndex++)
            {
                Entity member = members[memberIndex];
                if (!world.IsAlive(member) || !world.Has<AbilityStateBuffer>(member))
                {
                    continue;
                }

                ref AbilityStateBuffer baseSlots = ref world.Get<AbilityStateBuffer>(member);
                bool hasForm = world.Has<AbilityFormSlotBuffer>(member);
                AbilityFormSlotBuffer formSlots = hasForm ? world.Get<AbilityFormSlotBuffer>(member) : default;
                bool hasItemGranted = world.Has<ItemGrantedSlotBuffer>(member);
                ItemGrantedSlotBuffer itemGrantedSlots = hasItemGranted ? world.Get<ItemGrantedSlotBuffer>(member) : default;
                bool hasGranted = world.Has<GrantedSlotBuffer>(member);
                GrantedSlotBuffer grantedSlots = hasGranted ? world.Get<GrantedSlotBuffer>(member) : default;
                int ownerTemplateKeyId = world.Has<EntityTemplateKeyRef>(member)
                    ? world.Get<EntityTemplateKeyRef>(member).TemplateKeyId
                    : 0;

                for (int slotIndex = 0; slotIndex < AbilityStateBuffer.CAPACITY; slotIndex++)
                {
                    AbilitySlotState slot = AbilitySlotResolver.Resolve(
                        in baseSlots,
                        in formSlots,
                        hasForm,
                        in itemGrantedSlots,
                        hasItemGranted,
                        in grantedSlots,
                        hasGranted,
                        slotIndex);
                    if (slot.AbilityId <= 0 && slot.TemplateEntityId == 0)
                    {
                        continue;
                    }

                    var slotContext = new AbilityAggregationSlotContext(member, ownerTemplateKeyId, slotIndex);
                    long key = profile.Selector(in slot, in slotContext, abilities);
                    result.AppendMember(key, member, slotIndex);
                }
            }

            result.SortAndSeal();
            return result.GroupCount;
        }

        private CompiledProfile RequireInstalled(int profileId)
        {
            if (!IsInstalled(profileId))
            {
                throw new InvalidOperationException(
                    $"Aggregation profile id {profileId} ('{_profileIds.GetName(profileId)}') is not installed.");
            }

            return _profiles[profileId];
        }

        private void InstallProfile(AbilityAggregationProfileDefinition definition)
        {
            string groupBy = definition.GroupBy;
            int separator = groupBy.IndexOf('.');
            string prefix = separator < 0 ? groupBy : groupBy[..separator];
            string argument = separator < 0 ? string.Empty : groupBy[(separator + 1)..];

            if (!_selectorFactoriesByPrefix.TryGetValue(prefix, out AbilityAggregationKeySelectorFactory factory))
            {
                throw new InvalidOperationException(
                    $"Aggregation profile '{definition.Id}' declares unknown groupBy prefix '{prefix}' " +
                    $"(expression '{groupBy}'); register the prefix before install.");
            }

            AbilityAggregationKeySelector selector;
            try
            {
                selector = factory(argument);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Aggregation profile '{definition.Id}' groupBy '{groupBy}' failed to compile: {ex.Message}", ex);
            }

            if (selector == null)
            {
                throw new InvalidOperationException(
                    $"Aggregation profile '{definition.Id}' groupBy '{groupBy}' compiled to a null selector.");
            }

            int profileId = _profileIds.Register(definition.Id);
            if (profileId < _profiles.Length && _profiles[profileId] != null)
            {
                throw new InvalidOperationException($"Aggregation profile '{definition.Id}' is already installed.");
            }

            if (profileId >= _profiles.Length)
            {
                int next = _profiles.Length;
                while (next <= profileId)
                {
                    next *= 2;
                }

                Array.Resize(ref _profiles, next);
            }

            _profiles[profileId] = new CompiledProfile
            {
                Selector = selector,
                Overflow = definition.Overflow ?? string.Empty,
            };
        }

        /// <summary>
        /// <c>catalog.&lt;tagPrefix&gt;</c>: resolve the tag-name prefix to a tag bitset mask against
        /// <see cref="TagRegistry"/> at install (string work happens once here); evaluation intersects
        /// the ability's catalog tags with the mask in place (zero strings, zero copies).
        /// </summary>
        private static AbilityAggregationKeySelector CompileCatalogSelector(string tagPrefix)
        {
            if (string.IsNullOrWhiteSpace(tagPrefix))
            {
                throw new InvalidOperationException("groupBy 'catalog.' requires a non-empty tag prefix.");
            }

            string dottedPrefix = tagPrefix + ".";
            var mask = default(GameplayTagContainer);
            for (int tagId = 1; tagId <= GameplayTagContainer.MAX_TAG_ID; tagId++)
            {
                string name = TagRegistry.GetName(tagId);
                if (name.Length > 0 && name.StartsWith(dottedPrefix, StringComparison.Ordinal))
                {
                    mask.AddTag(tagId);
                }
            }

            return (in AbilitySlotState slot, in AbilityAggregationSlotContext _, AbilityDefinitionRegistry abilities) =>
            {
                if (slot.AbilityId > 0)
                {
                    int tagId = abilities.FirstCatalogTagIntersection(slot.AbilityId, in mask);
                    if (tagId != 0)
                    {
                        return AbilityAggregationKeyKinds.MakeKey(AbilityAggregationKeyKinds.CatalogTag, tagId);
                    }
                }

                return IdentityKey(in slot);
            };
        }

        /// <summary>
        /// <c>template.id</c>: one command layout cell per owner unit template and slot index.
        /// </summary>
        private static AbilityAggregationKeySelector CompileTemplateSelector(string argument)
        {
            if (!string.Equals(argument, "id", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"groupBy field '{argument}' is not supported; expected 'id'.");
            }

            return static (in AbilitySlotState _, in AbilityAggregationSlotContext context, AbilityDefinitionRegistry _) =>
            {
                if (context.OwnerTemplateKeyId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Aggregation profile 'template.id' requires owner entity {context.Owner} to have EntityTemplateKeyRef.");
                }

                return AbilityAggregationKeyKinds.MakeKey(
                    AbilityAggregationKeyKinds.OwnerTemplateSlot,
                    MakeOwnerTemplateSlotId(context.OwnerTemplateKeyId, context.SlotIndex));
            };
        }

        /// <summary><c>ability.id</c>: one command cell per ability definition id.</summary>
        private static AbilityAggregationKeySelector CompileAbilitySelector(string argument)
        {
            if (!string.Equals(argument, "id", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"groupBy field '{argument}' is not supported; expected 'id'.");
            }

            return static (in AbilitySlotState slot, in AbilityAggregationSlotContext _, AbilityDefinitionRegistry _) => IdentityKey(in slot);
        }

        private static int MakeOwnerTemplateSlotId(int ownerTemplateKeyId, int slotIndex)
        {
            if (ownerTemplateKeyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ownerTemplateKeyId));
            }

            if ((uint)slotIndex >= (uint)AbilityStateBuffer.CAPACITY)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }

            return ownerTemplateKeyId * AbilityStateBuffer.CAPACITY + slotIndex;
        }

        /// <summary>Per-ability identity: ability id when present, otherwise the backing template entity id.</summary>
        private static long IdentityKey(in AbilitySlotState slot)
        {
            return slot.AbilityId > 0
                ? AbilityAggregationKeyKinds.MakeKey(AbilityAggregationKeyKinds.AbilityId, slot.AbilityId)
                : AbilityAggregationKeyKinds.MakeKey(AbilityAggregationKeyKinds.TemplateEntityId, slot.TemplateEntityId);
        }

        private sealed class CompiledProfile
        {
            public AbilityAggregationKeySelector Selector;
            public string Overflow = string.Empty;
        }
    }
}
