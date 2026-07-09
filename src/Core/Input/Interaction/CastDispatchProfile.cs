using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>Built-in selector kinds (RFC-0065 §5.8, DEC-11). Registry keys, not a closed enum.</summary>
    public static class CastDispatchSelectorKinds
    {
        /// <summary>Every actor in the route group dispatches.</summary>
        public const string All = "all";

        /// <summary>The scorer's top <c>n</c> actors dispatch (requires a scorer).</summary>
        public const string TopN = "topN";

        /// <summary>One actor per trigger; a per-group cursor advances on <c>advanceOn</c> events.</summary>
        public const string Cycle = "cycle";
    }

    /// <summary>Built-in router kinds (RFC-0065 §5.8, DEC-11). Registry keys, not a closed enum.</summary>
    public static class CastDispatchRouterKinds
    {
        /// <summary>Resolved dispatch actors submit together; <c>sharedOrderId</c> declares shared fan-out.</summary>
        public const string Parallel = "parallel";

        /// <summary>Resolved dispatch actors submit one after another; incompatible with <c>sharedOrderId</c>.</summary>
        public const string Sequential = "sequential";
    }

    /// <summary>Built-in scorer kinds (RFC-0065 §5.8, DEC-9/DEC-11). Registry keys, not a closed enum.</summary>
    public static class CastDispatchScorerKinds
    {
        /// <summary>Consideration-based scoring; each entry is <c>considerationId[:modifier]</c>.</summary>
        public const string Utility = "utility";
    }

    /// <summary>Built-in consideration ids for the utility scorer (infrastructure primitives).</summary>
    public static class CastDispatchConsiderationIds
    {
        /// <summary>Ground-plane distance in cm from the actor's WorldPositionCm to the dispatch target.</summary>
        public const string DistanceToTarget = "distanceToTarget";
    }

    /// <summary>
    /// Evaluation context for one dispatch: the resolved target point in world centimeters
    /// (X/Z ground plane, matching <c>Order.Args.Spatial.WorldCm</c>), the world to read actor
    /// state from, and the caller-defined cycle state key (RFC: (frame, routeGroupKey) — the
    /// kernel treats it as opaque).
    /// </summary>
    public readonly struct CastDispatchContext
    {
        public CastDispatchContext(World world, Vector3 targetWorldCm, long groupKey)
        {
            World = world;
            TargetWorldCm = targetWorldCm;
            GroupKey = groupKey;
        }

        public World World { get; }
        public Vector3 TargetWorldCm { get; }
        public long GroupKey { get; }
    }

    /// <summary>Router semantics for one dispatch: shared order id fan-out and/or sequential submit.</summary>
    public readonly record struct CastDispatchRouting(bool SharedOrderId, bool Sequential);

    /// <summary>
    /// Selector evaluator (DEC-11 registry entry): writes the actors that should submit an order
    /// this trigger into <paramref name="selected"/> and returns the count. Must be steady-state
    /// allocation free. Profile-compiled parameters and the scorer are reached through the scope.
    /// </summary>
    public delegate int CastDispatchSelectorEvaluator(
        in CastDispatchSelectorScope scope,
        ReadOnlySpan<Entity> actors,
        in CastDispatchContext ctx,
        Span<Entity> selected);

    /// <summary>
    /// Install-time validator for a selector kind; throws on configuration errors
    /// (e.g. the built-in topN rejects a missing scorer — ranking without a basis).
    /// </summary>
    public delegate void CastDispatchSelectorInstallValidator(
        string profileId,
        CastDispatchSelectorDefinition definition,
        bool hasScorer);

    /// <summary>
    /// Per-actor scorer compiled from a profile's scorer declaration. Higher is better.
    /// Must be steady-state allocation free.
    /// </summary>
    public delegate float CastDispatchScorer(Entity actor, in CastDispatchContext ctx);

    /// <summary>
    /// Scorer-kind compiler (DEC-11 registry entry): lowers the declaration to a per-actor scorer
    /// at install time, resolving consideration ids against the registry's consideration table.
    /// </summary>
    public delegate CastDispatchScorer CastDispatchScorerCompiler(
        string profileId,
        CastDispatchScorerDefinition definition,
        CastDispatchProfileRegistry registry);

    /// <summary>Consideration evaluator (DEC-9 bridge surface): (actor, target point) → raw value.</summary>
    public delegate float CastDispatchConsiderationEvaluator(Entity actor, in CastDispatchContext ctx);

    /// <summary>
    /// Router-kind compiler (DEC-11 registry entry): lowers the declaration to routing semantics
    /// at install time; throws on contradictory configuration.
    /// </summary>
    public delegate CastDispatchRouting CastDispatchRouterCompiler(
        string profileId,
        CastDispatchRouterDefinition definition);

    /// <summary>
    /// Read surface handed to selector evaluators: the compiled selector parameters, the profile's
    /// scorer, and the per-group cycle cursor. Custom kinds compose these instead of reaching into
    /// registry internals.
    /// </summary>
    public readonly struct CastDispatchSelectorScope
    {
        private readonly CastDispatchProfileRegistry _registry;
        private readonly int _profileId;

        internal CastDispatchSelectorScope(CastDispatchProfileRegistry registry, int profileId, int n, bool hasScorer)
        {
            _registry = registry;
            _profileId = profileId;
            N = n;
            HasScorer = hasScorer;
        }

        /// <summary>The selector's declared <c>n</c> (0 when the kind takes none).</summary>
        public int N { get; }

        /// <summary>True when the profile declared a scorer.</summary>
        public bool HasScorer { get; }

        /// <summary>Score one actor with the profile's compiled scorer; higher is better.</summary>
        public float Score(Entity actor, in CastDispatchContext ctx) => _registry.ScoreActor(_profileId, actor, in ctx);

        /// <summary>Current cycle cursor for the group key (0 until the first advance).</summary>
        public int CycleCursor(long groupKey) => _registry.GetCycleCursor(_profileId, groupKey);
    }

    /// <summary>Merged root of <c>Input/cast_dispatch_profiles.json</c>.</summary>
    public sealed class CastDispatchProfilesConfig
    {
        public List<CastDispatchProfileDefinition> Profiles { get; set; }
    }

    /// <summary>One dispatch profile (RFC-0065 §5.8). Strings live only in JSON.</summary>
    public sealed class CastDispatchProfileDefinition
    {
        public string Id { get; set; } = string.Empty;
        public CastDispatchSelectorDefinition Selector { get; set; }
        public CastDispatchScorerDefinition Scorer { get; set; }
        public CastDispatchRouterDefinition Router { get; set; }
    }

    /// <summary>Selector declaration: a registry kind plus kind-specific parameters.</summary>
    public sealed class CastDispatchSelectorDefinition
    {
        public string Kind { get; set; } = string.Empty;

        /// <summary>topN: how many actors dispatch per trigger.</summary>
        public int? N { get; set; }

        /// <summary>cycle: event key that advances the cursor (registered into the advance event id space at install).</summary>
        public string AdvanceOn { get; set; }
    }

    /// <summary>Scorer declaration: a registry kind plus <c>considerationId[:modifier]</c> entries.</summary>
    public sealed class CastDispatchScorerDefinition
    {
        public string Kind { get; set; } = string.Empty;
        public List<string> Considerations { get; set; }
    }

    /// <summary>Router declaration: a registry kind plus kind-specific parameters.</summary>
    public sealed class CastDispatchRouterDefinition
    {
        public string Kind { get; set; } = string.Empty;

        /// <summary>parallel: all selected actors share one order id (shared fan-out).</summary>
        public bool? SharedOrderId { get; set; }
    }
}
