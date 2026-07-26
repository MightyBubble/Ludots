using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// CommandIntentProfile registry and evaluator (RFC-0065 INT-1/2/3, DEC-14). Profiles are declared
    /// in <c>Input/command_intent_profiles.json</c> and compiled at install time: predicate shorthands
    /// lower to tag bitsets and ids (single predicate evaluation path), stance names resolve through
    /// <see cref="DomainStanceQuery"/>, order type keys are validated against <see cref="OrderTypeRegistry"/>,
    /// and slot selectors compile to (kind, param id). Evaluation walks rules in descending priority and
    /// the first full predicate match wins — winning is final: a later failure to land the route must not
    /// fall through to lower-priority rules (no fallback). Steady-state evaluation is allocation free and
    /// performs zero string comparisons.
    /// <para>
    /// Knowledge gating (INT-2) is built into evaluation: entity facts the acting domain cannot know
    /// are demoted to a ground hit before any
    /// rule (including target domain resolution for stance predicates) sees them — per DEC-14 a unit
    /// invisible under fog must not be routable, so ground rules may still win but no entity predicate
    /// can. Assemblies that intentionally run without fog must pass an explicit no-op gate; a missing
    /// gate is a startup error, not an allow-all fallback.
    /// </para>
    /// </summary>
    public sealed class CommandIntentProfileRegistry
    {
        private readonly StringIntRegistry _profileIds;
        private readonly World _world;
        private readonly TagOps _tagOps;
        private readonly AbilityDefinitionRegistry _abilityDefinitions;
        private readonly ControlDomainQuery _controlDomains;
        private readonly DomainStanceQuery _stances;
        private readonly OrderTypeRegistry _orderTypes;
        private readonly CommandIntentTargetGate _targetGate;
        private readonly Dictionary<string, int> _groupPolicyIndexByKind = new(StringComparer.Ordinal);
        private readonly List<CommandIntentGroupPolicyApplier> _groupPolicies = new();

        private CompiledProfile[] _profiles = new CompiledProfile[8];

        private const string ByAbilityTagSelectorPrefix = "byAbilityTag:";
        private const string ContextGroupSelectorPrefix = "contextGroup:";

        public CommandIntentProfileRegistry(
            StringIntRegistry profileIdRegistry,
            World world,
            TagOps tagOps,
            AbilityDefinitionRegistry abilityDefinitions,
            ControlDomainQuery controlDomains,
            DomainStanceQuery stances,
            OrderTypeRegistry orderTypes,
            CommandIntentTargetGate targetGate)
        {
            _profileIds = profileIdRegistry ?? throw new ArgumentNullException(nameof(profileIdRegistry));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            _abilityDefinitions = abilityDefinitions ?? throw new ArgumentNullException(nameof(abilityDefinitions));
            _controlDomains = controlDomains ?? throw new ArgumentNullException(nameof(controlDomains));
            _stances = stances ?? throw new ArgumentNullException(nameof(stances));
            _orderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
            _targetGate = targetGate ?? throw new ArgumentNullException(nameof(targetGate));
            _groupPolicyIndexByKind.Add(CommandIntentGroupPolicyKinds.Independent, 0);
            _groupPolicies.Add(static (_, _, _, routedCount) => routedCount);
        }

        /// <summary>Profile id space; context frames reference command intent profiles by these ids.</summary>
        public StringIntRegistry ProfileIdRegistry => _profileIds;

        /// <summary>
        /// Register an additional group policy kind (DEC-11 registry extension point, e.g. <c>bySelector</c>).
        /// <see cref="CommandIntentGroupPolicyKinds.Independent"/> is built in and cannot be re-registered.
        /// </summary>
        public void RegisterGroupPolicy(string kind, CommandIntentGroupPolicyApplier applier)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Group policy kind is required.", nameof(kind));
            }

            kind = kind.Trim();
            if (_groupPolicyIndexByKind.ContainsKey(kind))
            {
                throw new InvalidOperationException($"Group policy kind '{kind}' is already registered.");
            }

            _groupPolicies.Add(applier ?? throw new ArgumentNullException(nameof(applier)));
            _groupPolicyIndexByKind.Add(kind, _groupPolicies.Count - 1);
        }

        /// <summary>
        /// Compile and install every profile in the config. Fails fast on duplicate priorities, unknown
        /// group policy kinds, unresolvable stance names, unknown order type keys, non-semantic slot
        /// selectors (DEC-14: bare slot indices are forbidden), and duplicate installs.
        /// </summary>
        public void Install(CommandIntentProfilesConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            CommandIntentProfileConfigLoader.Validate(config, nameof(CommandIntentProfilesConfig));
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

        /// <summary>
        /// Route a single actor: rules are evaluated in descending priority and the first rule whose
        /// actor and target predicates all hold wins. Returns false when no rule matches.
        /// <paramref name="actorDomainRep"/> is the actor's own control domain (proxy control still
        /// evaluates stance from the acting domain, DEC-14); pass <see cref="Entity.Null"/> when the
        /// actor has no domain — stance-requiring rules then never match.
        /// INT-2: when the injected gate rejects an entity fact for <paramref name="actorDomainRep"/>,
        /// the facts are demoted to a ground hit before evaluation — the target can never satisfy an
        /// entity predicate (equivalent to the entity not existing) but ground rules may still win, and
        /// target domain resolution for stance happens only after the gate passed.
        /// </summary>
        public bool TryRoute(
            int profileId,
            Entity actorEntity,
            Entity actorDomainRep,
            in CommandIntentTargetFacts facts,
            out CommandIntentRoute route)
        {
            CompiledProfile profile = RequireInstalled(profileId);
            CommandIntentTargetFacts gatedFacts = GateFacts(actorDomainRep, in facts);
            CompiledRule[] rules = profile.Rules;
            for (int i = 0; i < rules.Length; i++)
            {
                if (Matches(in rules[i], profile.StancePool, actorEntity, actorDomainRep, in gatedFacts))
                {
                    route = rules[i].Route;
                    return true;
                }
            }

            route = CommandIntentRoute.None;
            return false;
        }

        /// <summary>
        /// Route a group of actors: each actor is routed independently (its own domain rep resolved via
        /// <see cref="ControlDomainQuery"/>), then the profile's group policy adjusts the per-actor
        /// results. <paramref name="routesPerActor"/> is parallel to <paramref name="actors"/>; actors
        /// without a matching rule receive <see cref="CommandIntentRoute.None"/>. Returns the number of
        /// actors that keep a route. INT-2 gating applies per actor against its own domain rep (see
        /// <see cref="TryRoute"/>). Steady-state allocation free.
        /// </summary>
        public int RouteGroup(
            int profileId,
            ReadOnlySpan<Entity> actors,
            Entity anchorRep,
            in CommandIntentTargetFacts facts,
            Span<CommandIntentRoute> routesPerActor)
        {
            CompiledProfile profile = RequireInstalled(profileId);
            if (routesPerActor.Length < actors.Length)
            {
                throw new ArgumentException("Route buffer must cover every actor.", nameof(routesPerActor));
            }

            int routedCount = 0;
            for (int i = 0; i < actors.Length; i++)
            {
                if (!_controlDomains.TryResolveControlDomain(actors[i], out Entity actorDomainRep))
                {
                    actorDomainRep = Entity.Null;
                }

                if (TryRoute(profileId, actors[i], actorDomainRep, in facts, out CommandIntentRoute route))
                {
                    routesPerActor[i] = route;
                    routedCount++;
                }
                else
                {
                    routesPerActor[i] = CommandIntentRoute.None;
                }
            }

            CommandIntentGroupPolicyApplier applier = _groupPolicies[profile.GroupPolicyIndex];
            return applier(actors, anchorRep, routesPerActor[..actors.Length], routedCount);
        }

        /// <summary>
        /// INT-2 (DEC-14): an entity fact the viewer's domain cannot know demotes to a ground hit.
        /// Assemblies without fog use an explicit no-op target gate; null is never accepted.
        /// </summary>
        private CommandIntentTargetFacts GateFacts(Entity viewerRep, in CommandIntentTargetFacts facts)
        {
            if (!facts.HasEntity || _targetGate(viewerRep, facts.Target))
            {
                return facts;
            }

            return new CommandIntentTargetFacts(Entity.Null, HasEntity: false);
        }

        private bool Matches(
            in CompiledRule rule,
            int[] stancePool,
            Entity actorEntity,
            Entity actorDomainRep,
            in CommandIntentTargetFacts facts)
        {
            // Structural predicate first: hit classification is data (hasEntity tri-state), not a hidden enum.
            if (rule.HasEntity == 0 && facts.HasEntity)
            {
                return false;
            }

            if (rule.HasEntity == 1 && !facts.HasEntity)
            {
                return false;
            }

            if (rule.HasActorAllTags || rule.HasActorAnyTags)
            {
                if (!_world.IsAlive(actorEntity) || !_world.Has<GameplayTagContainer>(actorEntity))
                {
                    return false;
                }

                ref GameplayTagContainer actorTags = ref _world.Get<GameplayTagContainer>(actorEntity);
                if (rule.HasActorAllTags && !_tagOps.ContainsAll(ref actorTags, in rule.ActorAllTags, TagSense.Effective))
                {
                    return false;
                }

                if (rule.HasActorAnyTags && !_tagOps.Intersects(ref actorTags, in rule.ActorAnyTags, TagSense.Effective))
                {
                    return false;
                }
            }

            if (rule.ActorAbilityCatalogTagId != 0 && !HasAbilityWithCatalogTag(actorEntity, rule.ActorAbilityCatalogTagId))
            {
                return false;
            }

            if (rule.HasTargetAllTags || rule.HasTargetAnyTags)
            {
                if (!facts.HasEntity || !_world.IsAlive(facts.Target) || !_world.Has<GameplayTagContainer>(facts.Target))
                {
                    return false;
                }

                ref GameplayTagContainer targetTags = ref _world.Get<GameplayTagContainer>(facts.Target);
                if (rule.HasTargetAllTags && !_tagOps.ContainsAll(ref targetTags, in rule.TargetAllTags, TagSense.Effective))
                {
                    return false;
                }

                if (rule.HasTargetAnyTags && !_tagOps.Intersects(ref targetTags, in rule.TargetAnyTags, TagSense.Effective))
                {
                    return false;
                }
            }

            if (rule.StanceCount > 0)
            {
                // A stance-requiring rule never matches when either side's domain cannot be resolved.
                if (!facts.HasEntity || actorDomainRep == Entity.Null)
                {
                    return false;
                }

                if (!_controlDomains.TryResolveControlDomain(facts.Target, out Entity targetDomainRep))
                {
                    return false;
                }

                int stanceId = _stances.GetStance(actorDomainRep, targetDomainRep);
                bool stanceMatched = false;
                for (int i = 0; i < rule.StanceCount; i++)
                {
                    if (stancePool[rule.StanceOffset + i] == stanceId)
                    {
                        stanceMatched = true;
                        break;
                    }
                }

                if (!stanceMatched)
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasAbilityWithCatalogTag(Entity actor, int catalogTagId)
        {
            if (!_world.IsAlive(actor) || !_world.Has<AbilityStateBuffer>(actor))
            {
                return false;
            }

            ref AbilityStateBuffer abilities = ref _world.Get<AbilityStateBuffer>(actor);
            bool hasForm = _world.Has<AbilityFormSlotBuffer>(actor);
            AbilityFormSlotBuffer formSlots = hasForm ? _world.Get<AbilityFormSlotBuffer>(actor) : default;
            bool hasItemGranted = _world.Has<ItemGrantedSlotBuffer>(actor);
            ItemGrantedSlotBuffer itemGrantedSlots = hasItemGranted ? _world.Get<ItemGrantedSlotBuffer>(actor) : default;
            bool hasGranted = _world.Has<GrantedSlotBuffer>(actor);
            GrantedSlotBuffer grantedSlots = hasGranted ? _world.Get<GrantedSlotBuffer>(actor) : default;

            for (int slotIndex = 0; slotIndex < AbilityStateBuffer.CAPACITY; slotIndex++)
            {
                AbilitySlotState slot = AbilitySlotResolver.Resolve(
                    in abilities,
                    in formSlots,
                    hasForm,
                    in itemGrantedSlots,
                    hasItemGranted,
                    in grantedSlots,
                    hasGranted,
                    slotIndex);
                if (slot.AbilityId > 0 && _abilityDefinitions.HasCatalogTag(slot.AbilityId, catalogTagId))
                {
                    return true;
                }
            }

            return false;
        }

        private CompiledProfile RequireInstalled(int profileId)
        {
            if (!IsInstalled(profileId))
            {
                throw new InvalidOperationException(
                    $"Command intent profile id {profileId} ('{_profileIds.GetName(profileId)}') is not installed.");
            }

            return _profiles[profileId];
        }

        private void InstallProfile(CommandIntentProfileDefinition definition)
        {
            if (!_groupPolicyIndexByKind.TryGetValue(definition.GroupPolicy.Kind, out int groupPolicyIndex))
            {
                throw new InvalidOperationException(
                    $"Command intent profile '{definition.Id}' declares unknown group policy kind '{definition.GroupPolicy.Kind}'.");
            }

            int profileId = _profileIds.Register(definition.Id);
            if (profileId < _profiles.Length && _profiles[profileId] != null)
            {
                throw new InvalidOperationException($"Command intent profile '{definition.Id}' is already installed.");
            }

            var sorted = new List<CommandIntentRuleDefinition>(definition.Rules);
            // Duplicate priorities already failed validation, so the descending order is total.
            sorted.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));

            var rules = new CompiledRule[sorted.Count];
            var stancePool = new List<int>();
            for (int i = 0; i < sorted.Count; i++)
            {
                rules[i] = CompileRule(definition.Id, sorted[i], ruleIndex: i, stancePool);
            }

            var profile = new CompiledProfile
            {
                GroupPolicyIndex = groupPolicyIndex,
                Rules = rules,
                StancePool = stancePool.ToArray(),
            };

            if (profileId >= _profiles.Length)
            {
                int next = _profiles.Length;
                while (next <= profileId)
                {
                    next *= 2;
                }

                Array.Resize(ref _profiles, next);
            }

            _profiles[profileId] = profile;
        }

        private CompiledRule CompileRule(
            string profileId,
            CommandIntentRuleDefinition definition,
            int ruleIndex,
            List<int> stancePool)
        {
            var rule = new CompiledRule
            {
                Priority = definition.Priority,
                HasEntity = -1,
            };

            CommandIntentActorPredicateDefinition actor = definition.Actor;
            if (actor != null)
            {
                rule.HasActorAllTags = TryBuildMask(profileId, actor.AllTags, ref rule.ActorAllTags);
                rule.HasActorAnyTags = TryBuildMask(profileId, actor.AnyTags, ref rule.ActorAnyTags);
                if (!string.IsNullOrWhiteSpace(actor.HasAbilityWithTag))
                {
                    rule.ActorAbilityCatalogTagId = ResolveTagId(profileId, actor.HasAbilityWithTag);
                }
            }

            CommandIntentTargetPredicateDefinition target = definition.Target;
            if (target != null)
            {
                rule.HasTargetAllTags = TryBuildMask(profileId, target.AllTags, ref rule.TargetAllTags);
                rule.HasTargetAnyTags = TryBuildMask(profileId, target.AnyTags, ref rule.TargetAnyTags);
                if (target.HasEntity.HasValue)
                {
                    rule.HasEntity = target.HasEntity.Value ? (sbyte)1 : (sbyte)0;
                }

                if (target.Stance is { Count: > 0 })
                {
                    rule.StanceOffset = stancePool.Count;
                    rule.StanceCount = target.Stance.Count;
                    for (int i = 0; i < target.Stance.Count; i++)
                    {
                        if (!_stances.TryResolveStanceId(target.Stance[i], out int stanceId))
                        {
                            throw new InvalidOperationException(
                                $"Command intent profile '{profileId}' references unknown stance '{target.Stance[i]}'.");
                        }

                        stancePool.Add(stanceId);
                    }
                }
            }

            CommandIntentTargetShape targetShape = definition.Route.TargetShape!.Value;
            if (RequiresEntity(targetShape) && CanMatchGround(definition.Target))
            {
                throw new InvalidOperationException(
                    $"Command intent profile '{profileId}' rule {ruleIndex} declares target shape '{targetShape}' " +
                    "but its target predicate can match a ground hit.");
            }

            rule.Route = CompileRoute(profileId, definition.Route, ruleIndex, targetShape);
            return rule;
        }

        private CommandIntentRoute CompileRoute(
            string profileId,
            CommandIntentRouteDefinition definition,
            int ruleIndex,
            CommandIntentTargetShape targetShape)
        {
            if (!_orderTypes.TryGetId(definition.OrderTypeKey, out int orderTypeId))
            {
                throw new InvalidOperationException(
                    $"Command intent profile '{profileId}' references unknown order type key '{definition.OrderTypeKey}'.");
            }

            string slot = definition.Slot;
            if (string.IsNullOrWhiteSpace(slot))
            {
                return new CommandIntentRoute(
                    ruleIndex,
                    orderTypeId,
                    CommandIntentRouteKinds.None,
                    0,
                    targetShape);
            }

            if (slot.StartsWith(ByAbilityTagSelectorPrefix, StringComparison.Ordinal))
            {
                string tagName = slot[ByAbilityTagSelectorPrefix.Length..];
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    throw new InvalidOperationException(
                        $"Command intent profile '{profileId}' slot selector '{slot}' is missing the ability catalog tag.");
                }

                return new CommandIntentRoute(
                    ruleIndex,
                    orderTypeId,
                    CommandIntentRouteKinds.ByAbilityTag,
                    ResolveTagId(profileId, tagName),
                    targetShape);
            }

            if (slot.StartsWith(ContextGroupSelectorPrefix, StringComparison.Ordinal))
            {
                string groupName = slot[ContextGroupSelectorPrefix.Length..];
                if (string.IsNullOrWhiteSpace(groupName))
                {
                    throw new InvalidOperationException(
                        $"Command intent profile '{profileId}' slot selector '{slot}' is missing the context group id.");
                }

                int groupId = ContextGroupIdRegistry.GetId(groupName);
                if (groupId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Command intent profile '{profileId}' slot selector '{slot}' references unknown context group '{groupName}'. " +
                        "Declare it in GAS/context_groups.json before command intent profiles are loaded.");
                }

                return new CommandIntentRoute(
                    ruleIndex,
                    orderTypeId,
                    CommandIntentRouteKinds.ContextGroup,
                    groupId,
                    targetShape);
            }

            // DEC-14: semantic routing forbids bare slot indices (bySlotIndex / slotN / anything else).
            throw new InvalidOperationException(
                $"Command intent profile '{profileId}' slot selector '{slot}' is not a semantic selector; " +
                $"only '{ByAbilityTagSelectorPrefix}<tag>' and '{ContextGroupSelectorPrefix}<id>' are allowed.");
        }

        private static bool RequiresEntity(CommandIntentTargetShape targetShape) =>
            targetShape is CommandIntentTargetShape.Entity or CommandIntentTargetShape.WorldPositionAndEntity;

        private static bool CanMatchGround(CommandIntentTargetPredicateDefinition target)
        {
            if (target == null)
            {
                return true;
            }

            if (target.HasEntity == true)
            {
                return false;
            }

            return (target.AllTags == null || target.AllTags.Count == 0) &&
                   (target.AnyTags == null || target.AnyTags.Count == 0) &&
                   (target.Stance == null || target.Stance.Count == 0);
        }

        private static bool TryBuildMask(string profileId, List<string> tags, ref GameplayTagContainer mask)
        {
            if (tags == null || tags.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                mask.AddTag(ResolveTagId(profileId, tags[i]));
            }

            return true;
        }

        private static int ResolveTagId(string profileId, string tagName)
        {
            int id = TagRegistry.GetId(tagName);
            if (id != TagRegistry.InvalidId)
            {
                return id;
            }

            if (TagRegistry.IsFrozen)
            {
                throw new InvalidOperationException(
                    $"Command intent profile '{profileId}' references unknown tag '{tagName}' (tag registry is frozen).");
            }

            // Load-time declaration into the shared tag id space (same precedent as FilterProfileRegistry).
            return TagRegistry.Register(tagName);
        }

        /// <summary>Compiled rule row (SoA within the profile); all predicate data is ids and bitsets.</summary>
        private struct CompiledRule
        {
            public int Priority;
            public GameplayTagContainer ActorAllTags;
            public GameplayTagContainer ActorAnyTags;
            public bool HasActorAllTags;
            public bool HasActorAnyTags;
            public int ActorAbilityCatalogTagId;
            public GameplayTagContainer TargetAllTags;
            public GameplayTagContainer TargetAnyTags;
            public bool HasTargetAllTags;
            public bool HasTargetAnyTags;
            public int StanceOffset;
            public int StanceCount;
            /// <summary>Tri-state: -1 unspecified (matches both), 0 ground only, 1 entity only.</summary>
            public sbyte HasEntity;
            public CommandIntentRoute Route;
        }

        private sealed class CompiledProfile
        {
            public int GroupPolicyIndex;
            public CompiledRule[] Rules = Array.Empty<CompiledRule>();
            public int[] StancePool = Array.Empty<int>();
        }
    }
}
