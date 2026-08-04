using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// CastDispatchProfile registry and evaluator (RFC-0065 DSP-1/2/4, DEC-9/DEC-11). Profiles are
    /// declared in <c>Input/cast_dispatch_profiles.json</c> and compiled at install time: selector,
    /// scorer, and router kinds resolve through delegate tables (registry entries, never closed
    /// enums), scorer considerations lower to a consideration-id → delegate table, and
    /// <c>advanceOn</c> event keys register into the shared advance event id space. Unknown kinds,
    /// contradictory router parameters, and a topN selector without a scorer (ranking without a
    /// basis) all fail fast at install. Steady-state evaluation is allocation free.
    /// <para>
    /// Cycle state is keyed by the caller-supplied group key (RFC: (frame, routeGroupKey) — the
    /// kernel treats it as opaque). This kernel wires no event source: the caller observes accepted
    /// orders and calls <see cref="NotifyAdvance"/> explicitly.
    /// </para>
    /// </summary>
    public sealed class CastDispatchProfileRegistry
    {
        private const char ConsiderationModifierSeparator = ':';
        private const string InvertModifier = "invert";

        private readonly StringIntRegistry _profileIds;
        private readonly StringIntRegistry _advanceEventIds;
        private readonly Dictionary<string, SelectorEntry> _selectorsByKind = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CastDispatchRouterCompiler> _routersByKind = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CastDispatchScorerCompiler> _scorerKindsByName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CastDispatchConsiderationEvaluator> _considerationsById = new(StringComparer.Ordinal);

        private CompiledProfile[] _profiles = new CompiledProfile[8];
        private float[] _scoreScratch = new float[16];
        private bool[] _pickedScratch = new bool[16];

        public CastDispatchProfileRegistry(StringIntRegistry profileIdRegistry, StringIntRegistry advanceEventIdRegistry)
        {
            _profileIds = profileIdRegistry ?? throw new ArgumentNullException(nameof(profileIdRegistry));
            _advanceEventIds = advanceEventIdRegistry ?? throw new ArgumentNullException(nameof(advanceEventIdRegistry));

            _selectorsByKind.Add(CastDispatchSelectorKinds.All, new SelectorEntry(SelectAll, validator: null));
            _selectorsByKind.Add(CastDispatchSelectorKinds.TopN, new SelectorEntry(SelectTopN, ValidateTopNSelector));
            _selectorsByKind.Add(CastDispatchSelectorKinds.Cycle, new SelectorEntry(SelectCycle, ValidateCycleSelector));
            _routersByKind.Add(CastDispatchRouterKinds.Parallel, CompileParallelRouter);
            _routersByKind.Add(CastDispatchRouterKinds.Sequential, CompileSequentialRouter);
            _scorerKindsByName.Add(CastDispatchScorerKinds.Utility, CompileUtilityScorer);
            _considerationsById.Add(CastDispatchConsiderationIds.DistanceToTarget, EvaluateDistanceToTarget);
        }

        /// <summary>Profile id space; slot bindings reference dispatch profiles by these ids.</summary>
        public StringIntRegistry ProfileIdRegistry => _profileIds;

        /// <summary>Advance event key space; <c>advanceOn</c> keys register here at install.</summary>
        public StringIntRegistry AdvanceEventIdRegistry => _advanceEventIds;

        /// <summary>Register an additional selector kind (DEC-11 registry extension point).</summary>
        public void RegisterSelector(
            string kind,
            CastDispatchSelectorEvaluator evaluator,
            CastDispatchSelectorInstallValidator? validator = null)
        {
            kind = RequireNewKind(kind, _selectorsByKind.ContainsKey, "Selector");
            _selectorsByKind.Add(kind, new SelectorEntry(
                evaluator ?? throw new ArgumentNullException(nameof(evaluator)),
                validator));
        }

        /// <summary>Register an additional router kind (DEC-11 registry extension point).</summary>
        public void RegisterRouter(string kind, CastDispatchRouterCompiler compiler)
        {
            kind = RequireNewKind(kind, _routersByKind.ContainsKey, "Router");
            _routersByKind.Add(kind, compiler ?? throw new ArgumentNullException(nameof(compiler)));
        }

        /// <summary>Register an additional scorer kind (DEC-11 registry extension point).</summary>
        public void RegisterScorerKind(string kind, CastDispatchScorerCompiler compiler)
        {
            kind = RequireNewKind(kind, _scorerKindsByName.ContainsKey, "Scorer");
            _scorerKindsByName.Add(kind, compiler ?? throw new ArgumentNullException(nameof(compiler)));
        }

        /// <summary>
        /// Register a consideration evaluator (DEC-9 bridge surface). Scorer kinds resolve
        /// consideration ids against this table at install time.
        /// </summary>
        public void RegisterConsideration(string id, CastDispatchConsiderationEvaluator evaluator)
        {
            id = RequireNewKind(id, _considerationsById.ContainsKey, "Consideration");
            _considerationsById.Add(id, evaluator ?? throw new ArgumentNullException(nameof(evaluator)));
        }

        /// <summary>Resolve a consideration id; throws when unregistered (load-time fail fast).</summary>
        public CastDispatchConsiderationEvaluator RequireConsideration(string profileId, string considerationId)
        {
            if (!_considerationsById.TryGetValue(considerationId, out CastDispatchConsiderationEvaluator evaluator))
            {
                throw new InvalidOperationException(
                    $"Cast dispatch profile '{profileId}' references unknown consideration '{considerationId}'.");
            }

            return evaluator;
        }

        /// <summary>
        /// Compile and install every profile in the config. Fails fast on unknown selector/scorer/
        /// router kinds, kind-specific parameter errors (topN without scorer or with n &lt; 1, cycle
        /// without advanceOn, sequential with sharedOrderId), and duplicate installs.
        /// </summary>
        public void Install(CastDispatchProfilesConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            CastDispatchProfileConfigLoader.Validate(config, nameof(CastDispatchProfilesConfig));
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
        /// Evaluate one dispatch: writes the actors that should submit an order this trigger into
        /// <paramref name="dispatchTargets"/> (which must cover every actor) and returns the count;
        /// <paramref name="routing"/> carries the profile's router semantics. Read-only with respect
        /// to cycle state — the cursor moves only via <see cref="NotifyAdvance"/>. Steady-state
        /// allocation free.
        /// </summary>
        public int SelectDispatchTargets(
            int profileId,
            ReadOnlySpan<Entity> actors,
            in CastDispatchContext ctx,
            Span<Entity> dispatchTargets,
            out CastDispatchRouting routing)
        {
            CompiledProfile profile = RequireInstalled(profileId);
            if (dispatchTargets.Length < actors.Length)
            {
                throw new ArgumentException("Dispatch target buffer must cover every actor.", nameof(dispatchTargets));
            }

            routing = profile.Routing;
            var scope = new CastDispatchSelectorScope(this, profileId, profile.SelectorN, profile.Scorer != null);
            return profile.Selector(in scope, actors, in ctx, dispatchTargets);
        }

        /// <summary>
        /// Advance the profile's cycle cursor for one group when the event matches the profile's
        /// compiled <c>advanceOn</c> id; a mismatch or a profile without cycle state is a no-op.
        /// </summary>
        public void NotifyAdvance(int profileId, long groupKey, int advanceEventId)
        {
            CompiledProfile profile = RequireInstalled(profileId);
            if (profile.CycleCursors == null || advanceEventId == 0 || advanceEventId != profile.AdvanceEventId)
            {
                return;
            }

            ref int cursor = ref CollectionsMarshal.GetValueRefOrAddDefault(profile.CycleCursors, groupKey, out _);
            cursor++;
        }

        /// <summary>
        /// Drop the cycle cursor for a retired group key so per-group state never outlives the
        /// route group that owns it (the kernel has no event source to observe retirement itself).
        /// </summary>
        public void ResetCycle(int profileId, long groupKey)
        {
            CompiledProfile profile = RequireInstalled(profileId);
            profile.CycleCursors?.Remove(groupKey);
        }

        internal float ScoreActor(int profileId, Entity actor, in CastDispatchContext ctx)
        {
            CompiledProfile profile = RequireInstalled(profileId);
            if (profile.Scorer == null)
            {
                throw new InvalidOperationException(
                    $"Cast dispatch profile id {profileId} ('{_profileIds.GetName(profileId)}') declares no scorer.");
            }

            return profile.Scorer(actor, in ctx);
        }

        internal int GetCycleCursor(int profileId, long groupKey)
        {
            CompiledProfile profile = RequireInstalled(profileId);
            return profile.CycleCursors != null && profile.CycleCursors.TryGetValue(groupKey, out int cursor)
                ? cursor
                : 0;
        }

        private CompiledProfile RequireInstalled(int profileId)
        {
            if (!IsInstalled(profileId))
            {
                throw new InvalidOperationException(
                    $"Cast dispatch profile id {profileId} ('{_profileIds.GetName(profileId)}') is not installed.");
            }

            return _profiles[profileId];
        }

        private void InstallProfile(CastDispatchProfileDefinition definition)
        {
            if (!_selectorsByKind.TryGetValue(definition.Selector.Kind, out SelectorEntry selectorEntry))
            {
                throw new InvalidOperationException(
                    $"Cast dispatch profile '{definition.Id}' declares unknown selector kind '{definition.Selector.Kind}'.");
            }

            if (!_routersByKind.TryGetValue(definition.Router.Kind, out CastDispatchRouterCompiler routerCompiler))
            {
                throw new InvalidOperationException(
                    $"Cast dispatch profile '{definition.Id}' declares unknown router kind '{definition.Router.Kind}'.");
            }

            CastDispatchScorer? scorer = null;
            if (definition.Scorer != null)
            {
                if (!_scorerKindsByName.TryGetValue(definition.Scorer.Kind, out CastDispatchScorerCompiler scorerCompiler))
                {
                    throw new InvalidOperationException(
                        $"Cast dispatch profile '{definition.Id}' declares unknown scorer kind '{definition.Scorer.Kind}'.");
                }

                scorer = scorerCompiler(definition.Id, definition.Scorer, this)
                    ?? throw new InvalidOperationException(
                        $"Cast dispatch profile '{definition.Id}' scorer kind '{definition.Scorer.Kind}' compiled to null.");
            }

            selectorEntry.Validator?.Invoke(definition.Id, definition.Selector, scorer != null);

            int profileId = _profileIds.Register(definition.Id);
            if (profileId < _profiles.Length && _profiles[profileId] != null)
            {
                throw new InvalidOperationException($"Cast dispatch profile '{definition.Id}' is already installed.");
            }

            bool hasAdvance = !string.IsNullOrWhiteSpace(definition.Selector.AdvanceOn);
            var profile = new CompiledProfile
            {
                Selector = selectorEntry.Evaluator,
                SelectorN = definition.Selector.N ?? 0,
                AdvanceEventId = hasAdvance ? _advanceEventIds.Register(definition.Selector.AdvanceOn) : 0,
                Scorer = scorer,
                Routing = routerCompiler(definition.Id, definition.Router),
                CycleCursors = hasAdvance ? new Dictionary<long, int>() : null,
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

        private static string RequireNewKind(string kind, Func<string, bool> exists, string category)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException($"{category} kind is required.", nameof(kind));
            }

            kind = kind.Trim();
            if (exists(kind))
            {
                throw new InvalidOperationException($"{category} kind '{kind}' is already registered.");
            }

            return kind;
        }

        private static int SelectAll(
            in CastDispatchSelectorScope scope,
            ReadOnlySpan<Entity> actors,
            in CastDispatchContext ctx,
            Span<Entity> selected)
        {
            actors.CopyTo(selected);
            return actors.Length;
        }

        private int SelectTopN(
            in CastDispatchSelectorScope scope,
            ReadOnlySpan<Entity> actors,
            in CastDispatchContext ctx,
            Span<Entity> selected)
        {
            int count = actors.Length;
            if (count == 0)
            {
                return 0;
            }

            EnsureScoreScratch(count);
            for (int i = 0; i < count; i++)
            {
                _scoreScratch[i] = scope.Score(actors[i], in ctx);
                _pickedScratch[i] = false;
            }

            // Stable selection: score desc, then entity id asc breaks ties
            // (same tie chain style as ContextScoredOrderResolver).
            int take = Math.Min(scope.N, count);
            for (int rank = 0; rank < take; rank++)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                {
                    if (_pickedScratch[i])
                    {
                        continue;
                    }

                    if (best < 0 || IsBetterCandidate(_scoreScratch[i], actors[i], _scoreScratch[best], actors[best]))
                    {
                        best = i;
                    }
                }

                _pickedScratch[best] = true;
                selected[rank] = actors[best];
            }

            return take;
        }

        private static int SelectCycle(
            in CastDispatchSelectorScope scope,
            ReadOnlySpan<Entity> actors,
            in CastDispatchContext ctx,
            Span<Entity> selected)
        {
            if (actors.Length == 0)
            {
                return 0;
            }

            // Cursor modulo the live member count keeps the pointer valid across membership
            // changes (dead members removed by the caller shrink the span, never break the cycle).
            int cursor = scope.CycleCursor(ctx.GroupKey);
            selected[0] = actors[(int)((uint)cursor % (uint)actors.Length)];
            return 1;
        }

        private static void ValidateTopNSelector(string profileId, CastDispatchSelectorDefinition definition, bool hasScorer)
        {
            if (definition.N is not > 0)
            {
                throw new InvalidOperationException(
                    $"Cast dispatch profile '{profileId}' topN selector requires n >= 1.");
            }

            if (!hasScorer)
            {
                // Ranking without a scoring basis is a configuration error, not a runtime default.
                throw new InvalidOperationException(
                    $"Cast dispatch profile '{profileId}' topN selector requires a scorer.");
            }
        }

        private static void ValidateCycleSelector(string profileId, CastDispatchSelectorDefinition definition, bool hasScorer)
        {
            if (string.IsNullOrWhiteSpace(definition.AdvanceOn))
            {
                throw new InvalidOperationException(
                    $"Cast dispatch profile '{profileId}' cycle selector requires advanceOn.");
            }
        }

        private static CastDispatchRouting CompileParallelRouter(string profileId, CastDispatchRouterDefinition definition)
        {
            return new CastDispatchRouting(SharedOrderId: definition.SharedOrderId ?? false, Sequential: false);
        }

        private static CastDispatchRouting CompileSequentialRouter(string profileId, CastDispatchRouterDefinition definition)
        {
            if (definition.SharedOrderId == true)
            {
                throw new InvalidOperationException(
                    $"Cast dispatch profile '{profileId}' sequential router cannot request atomic fan-out.");
            }

            return new CastDispatchRouting(SharedOrderId: false, Sequential: true);
        }

        private static CastDispatchScorer CompileUtilityScorer(
            string profileId,
            CastDispatchScorerDefinition definition,
            CastDispatchProfileRegistry registry)
        {
            var evaluators = new CastDispatchConsiderationEvaluator[definition.Considerations.Count];
            var signs = new float[definition.Considerations.Count];
            for (int i = 0; i < definition.Considerations.Count; i++)
            {
                string entry = definition.Considerations[i];
                int separator = entry.IndexOf(ConsiderationModifierSeparator);
                string considerationId = separator < 0 ? entry : entry[..separator];
                evaluators[i] = registry.RequireConsideration(profileId, considerationId);

                signs[i] = 1f;
                if (separator >= 0)
                {
                    string modifier = entry[(separator + 1)..];
                    if (!string.Equals(modifier, InvertModifier, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Cast dispatch profile '{profileId}' consideration '{entry}' uses unknown modifier '{modifier}'.");
                    }

                    // Invert flips the ordering by negation; only relative rank matters here.
                    signs[i] = -1f;
                }
            }

            return (Entity actor, in CastDispatchContext ctx) =>
            {
                float total = 0f;
                for (int i = 0; i < evaluators.Length; i++)
                {
                    total += signs[i] * evaluators[i](actor, in ctx);
                }

                return total;
            };
        }

        private static float EvaluateDistanceToTarget(Entity actor, in CastDispatchContext ctx)
        {
            if (ctx.World == null || !ctx.World.TryGet(actor, out WorldPositionCm position))
            {
                // No position reads as farthest so positioned actors always outrank it.
                return float.MaxValue;
            }

            WorldCmInt2 actorCm = position.Value.ToWorldCmInt2();
            float dx = ctx.TargetWorldCm.X - actorCm.X;
            float dy = ctx.TargetWorldCm.Z - actorCm.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsBetterCandidate(float score, Entity entity, float bestScore, Entity bestEntity)
        {
            if (score > bestScore)
            {
                return true;
            }

            if (score < bestScore)
            {
                return false;
            }

            return CompareEntityId(entity, bestEntity) < 0;
        }

        private static int CompareEntityId(Entity left, Entity right)
        {
            int id = left.Id.CompareTo(right.Id);
            return id != 0 ? id : left.WorldId.CompareTo(right.WorldId);
        }

        private void EnsureScoreScratch(int count)
        {
            if (_scoreScratch.Length >= count)
            {
                return;
            }

            int next = _scoreScratch.Length;
            while (next < count)
            {
                next *= 2;
            }

            Array.Resize(ref _scoreScratch, next);
            Array.Resize(ref _pickedScratch, next);
        }

        private readonly struct SelectorEntry
        {
            public SelectorEntry(CastDispatchSelectorEvaluator evaluator, CastDispatchSelectorInstallValidator validator)
            {
                Evaluator = evaluator;
                Validator = validator;
            }

            public CastDispatchSelectorEvaluator Evaluator { get; }
            public CastDispatchSelectorInstallValidator Validator { get; }
        }

        private sealed class CompiledProfile
        {
            public required CastDispatchSelectorEvaluator Selector;
            public int SelectorN;
            public int AdvanceEventId;
            public required CastDispatchScorer Scorer;
            public CastDispatchRouting Routing;
            public required Dictionary<long, int> CycleCursors;
        }
    }
}
