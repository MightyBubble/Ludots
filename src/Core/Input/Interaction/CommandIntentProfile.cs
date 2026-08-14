using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Input.Interaction
{
    public enum CommandIntentTargetShape : byte
    {
        None = 0,
        WorldPositionCm = 1,
        Entity = 2,
        WorldPositionAndEntity = 3,
    }

    /// <summary>Built-in group policy kinds (RFC-0065 DEC-14). Registry keys, not a closed enum.</summary>
    public static class CommandIntentGroupPolicyKinds
    {
        /// <summary>Every actor routes independently; per-actor results are kept as-is.</summary>
        public const string Independent = "independent";
    }

    /// <summary>
    /// Route slot selector kinds compiled from the <c>route.slot</c> selector expression
    /// (RFC-0065 DEC-14). Semantic routing only ever uses <see cref="ByAbilityTag"/> or
    /// <see cref="ContextGroup"/>; bare slot indices are rejected at load time.
    /// </summary>
    public static class CommandIntentRouteKinds
    {
        /// <summary>No slot selector; the route carries only the order type.</summary>
        public const int None = 0;

        /// <summary>Slot located by ability catalog tag; <c>RouteParamId</c> is the tag id.</summary>
        public const int ByAbilityTag = 1;

        /// <summary>Slot delegated to ContextScored evaluation; <c>RouteParamId</c> is the context group id.</summary>
        public const int ContextGroup = 2;
    }

    /// <summary>
    /// Target-side facts a pointer intent evaluates against (RFC-0065 DEC-14).
    /// <c>HasEntity == false</c> means a ground hit; <see cref="Target"/> is then <see cref="Entity.Null"/>.
    /// Caller responsibility (INT-2): facts must already be viewer-knowledge gated — an entity the
    /// viewer cannot know about must never be presented here as a target.
    /// </summary>
    public readonly record struct CommandIntentTargetFacts(Entity Target, bool HasEntity);

    /// <summary>
    /// A resolved route: the winning rule index (priority order within the compiled profile),
    /// the order type id, and the slot selector as (kind, param id). Slot landing (resolving the
    /// param to a concrete ability slot) is downstream dispatch work, not evaluation work.
    /// </summary>
    public readonly record struct CommandIntentRoute(
        int RuleIndex,
        int OrderTypeId,
        int RouteKind,
        int RouteParamId,
        CommandIntentTargetShape TargetShape)
    {
        /// <summary>Sentinel for "no rule matched" in group results.</summary>
        public static readonly CommandIntentRoute None = new(
            -1,
            0,
            CommandIntentRouteKinds.None,
            0,
            CommandIntentTargetShape.None);

        /// <summary>True when a rule won for this actor.</summary>
        public bool HasRoute => RuleIndex >= 0;
    }

    /// <summary>
    /// Group policy applier (RFC-0065 DEC-14 registry): adjusts per-actor routes after independent
    /// evaluation. Returns the number of actors that keep a route. The built-in
    /// <see cref="CommandIntentGroupPolicyKinds.Independent"/> policy keeps results as-is.
    /// </summary>
    public delegate int CommandIntentGroupPolicyApplier(
        ReadOnlySpan<Entity> actors,
        Entity anchorRep,
        Span<CommandIntentRoute> routesPerActor,
        int routedCount);

    /// <summary>
    /// Knowledge gate for target facts (RFC-0065 INT-2, DEC-14): true when <paramref name="viewerRep"/>
    /// is allowed to command-target <paramref name="target"/> (<c>CanTargetCommand</c> semantics).
    /// <paramref name="viewerRep"/> is the acting side's control domain rep — proxy control gates from
    /// the acting domain, matching stance evaluation.
    /// </summary>
    public delegate bool CommandIntentTargetGate(Entity viewerRep, Entity target);

    /// <summary>Merged root of <c>Input/command_intent_profiles.json</c>.</summary>
    public sealed class CommandIntentProfilesConfig
    {
        public List<CommandIntentProfileDefinition> Profiles { get; set; }
    }

    /// <summary>One pointer-intent routing profile (RFC-0065 §5.11). Strings live only in JSON.</summary>
    public sealed class CommandIntentProfileDefinition
    {
        public string Id { get; set; } = string.Empty;
        public CommandIntentGroupPolicyDefinition GroupPolicy { get; set; }
        public List<CommandIntentRuleDefinition> Rules { get; set; }
    }

    /// <summary>Profile-level group policy declaration; one pointer intent has one group semantic.</summary>
    public sealed class CommandIntentGroupPolicyDefinition
    {
        public string Kind { get; set; } = string.Empty;
    }

    /// <summary>One rule: actor predicate × target predicate → route, ranked by a unique priority.</summary>
    public sealed class CommandIntentRuleDefinition
    {
        public int Priority { get; set; }
        public CommandIntentActorPredicateDefinition Actor { get; set; }
        public CommandIntentTargetPredicateDefinition Target { get; set; }
        public CommandIntentRouteDefinition Route { get; set; }
    }

    /// <summary>
    /// Actor-side predicate shorthand; lowered to bitset/id checks at install time
    /// (single predicate evaluation path, DEC-14).
    /// </summary>
    public sealed class CommandIntentActorPredicateDefinition
    {
        public string HasAbilityWithTag { get; set; }
        public List<string> AllTags { get; set; }
        public List<string> AnyTags { get; set; }
    }

    /// <summary>
    /// Target-side predicate shorthand. <c>HasEntity</c> is tri-state: null matches both ground and
    /// entity hits, true/false match exactly. Stance names resolve to relationship type ids at install.
    /// </summary>
    public sealed class CommandIntentTargetPredicateDefinition
    {
        public List<string> AllTags { get; set; }
        public List<string> AnyTags { get; set; }
        public List<string> Stance { get; set; }
        public bool? HasEntity { get; set; }
    }

    /// <summary>
    /// Route declaration: an order type key (validated against OrderTypeRegistry at install) and an
    /// optional slot selector expression (<c>byAbilityTag:&lt;tag&gt;</c> or <c>contextGroup:&lt;id&gt;</c>).
    /// </summary>
    public sealed class CommandIntentRouteDefinition
    {
        public string OrderTypeKey { get; set; } = string.Empty;
        public string Slot { get; set; }
        public CommandIntentTargetShape? TargetShape { get; set; }
    }
}
