using System;
using System.Collections.Generic;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// CastCommitProfile registry and interaction op executor (RFC-0065 CTX-7, §5.5, DEC-11/DEC-13).
    /// Profiles are declared in <c>Input/cast_commit_profiles.json</c> and compiled at install time:
    /// op kinds resolve against the op registry (unknown kinds fail fast), <c>contextProfileId</c>
    /// references must be installed <see cref="InteractionContextProfileRegistry"/> rows, payload
    /// value sources resolve against the value source registry, and frame action names register into
    /// the action id space. Execution is a flat walk over compiled op rows — there is no state
    /// machine anywhere: the only client-side interaction state is the entity-mounted active
    /// context the frame ops write. Steady-state execution is allocation free.
    /// </summary>
    public sealed class CastCommitProfileRegistry
    {
        private readonly StringIntRegistry _profileIds;
        private readonly StringIntRegistry _actionIds;
        private readonly StringIntRegistry _payloadKeyIds;
        private readonly StringIntRegistry _payloadValueSourceIds;
        private readonly InteractionContextProfileRegistry _contextProfiles;
        private readonly Dictionary<string, int> _opIndexByKind = new(StringComparer.Ordinal);
        private readonly List<InteractionOpHandler> _opHandlers = new();
        private readonly int _pushFrameOpIndex;

        private CompiledProfile[] _profiles = new CompiledProfile[8];

        public CastCommitProfileRegistry(
            StringIntRegistry profileIdRegistry,
            StringIntRegistry actionIdRegistry,
            InteractionContextProfileRegistry contextProfiles)
        {
            _profileIds = profileIdRegistry ?? throw new ArgumentNullException(nameof(profileIdRegistry));
            _actionIds = actionIdRegistry ?? throw new ArgumentNullException(nameof(actionIdRegistry));
            _contextProfiles = contextProfiles ?? throw new ArgumentNullException(nameof(contextProfiles));
            _payloadKeyIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _payloadValueSourceIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _payloadValueSourceIds.Register(CastCommitPayloadValueSources.CursorWorld);
            _payloadValueSourceIds.Register(CastCommitPayloadValueSources.FramePointer);

            RegisterOp(InteractionOpKinds.PushFrame, ExecutePushFrame);
            RegisterOp(InteractionOpKinds.PopFrame, ExecutePopFrame);
            RegisterOp(InteractionOpKinds.SubmitOrder, ExecuteSubmitOrder);
            _pushFrameOpIndex = _opIndexByKind[InteractionOpKinds.PushFrame];
        }

        /// <summary>Profile id space; cast preference resolution references profiles by these ids.</summary>
        public StringIntRegistry ProfileIdRegistry => _profileIds;

        /// <summary>Input action id space frame action names register into at install.</summary>
        public StringIntRegistry ActionIdRegistry => _actionIds;

        /// <summary>Order argument slot key id space (payload map keys).</summary>
        public StringIntRegistry PayloadKeyRegistry => _payloadKeyIds;

        /// <summary>Payload value source id space; wiring/mods register sources before install.</summary>
        public StringIntRegistry PayloadValueSourceRegistry => _payloadValueSourceIds;

        /// <summary>
        /// Register an additional op kind (DEC-11 registry extension point). Built-in kinds cannot
        /// be re-registered.
        /// </summary>
        public void RegisterOp(string kind, InteractionOpHandler handler)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Interaction op kind is required.", nameof(kind));
            }

            kind = kind.Trim();
            if (_opIndexByKind.ContainsKey(kind))
            {
                throw new InvalidOperationException($"Interaction op kind '{kind}' is already registered.");
            }

            _opHandlers.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            _opIndexByKind.Add(kind, _opHandlers.Count - 1);
        }

        /// <summary>Register a payload value source name (idempotent) and return its id.</summary>
        public int RegisterPayloadValueSource(string name)
        {
            return _payloadValueSourceIds.Register(name);
        }

        /// <summary>
        /// Compile and install every profile in the config. Fails fast on unknown op kinds,
        /// <c>pushFrame</c> ops without an installed context profile, unknown payload value sources,
        /// and duplicate installs.
        /// </summary>
        public void Install(CastCommitProfilesConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            CastCommitProfileConfigLoader.Validate(config, nameof(CastCommitProfilesConfig));
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                InstallProfile(config.Profiles[i]);
            }
        }

        /// <summary>True when the profile id has been compiled and can execute.</summary>
        public bool IsInstalled(int profileId)
        {
            return profileId > 0 && profileId < _profiles.Length && _profiles[profileId] != null;
        }

        /// <summary>Execute the profile's <c>onActivate</c> op sequence.</summary>
        public void ExecuteActivation(int profileId, in InteractionOpContext ctx)
        {
            CompiledProfile profile = RequireInstalled(profileId);
            ExecuteOps(profile, profile.OnActivate, in ctx);
        }

        /// <summary>
        /// Execute the op sequence the profile binds to <paramref name="actionId"/>. Returns false
        /// when the profile declares no ops for that action (the action is not intercepted).
        /// </summary>
        public bool TryExecuteFrameAction(int profileId, int actionId, in InteractionOpContext ctx)
        {
            CompiledProfile profile = RequireInstalled(profileId);
            int[] actionIds = profile.FrameActionIds;
            for (int i = 0; i < actionIds.Length; i++)
            {
                if (actionIds[i] == actionId)
                {
                    ExecuteOps(profile, profile.FrameActionOps[i], in ctx);
                    return true;
                }
            }

            return false;
        }

        private void ExecuteOps(CompiledProfile profile, CompiledOp[] ops, in InteractionOpContext ctx)
        {
            for (int i = 0; i < ops.Length; i++)
            {
                var args = new InteractionOpArgs(
                    ops[i].ContextProfileId,
                    new CastCommitOrderPayload(profile.PayloadPool, ops[i].PayloadOffset, ops[i].PayloadCount));
                _opHandlers[ops[i].OpIndex](in ctx, in args);
            }
        }

        private void ExecutePushFrame(in InteractionOpContext ctx, in InteractionOpArgs args)
        {
            if (!_contextProfiles.TryCreateActiveContext(
                    args.ContextProfileId,
                    ctx.ContextEntity,
                    ActiveInteractionContextSource.CastCommitOp,
                    out ActiveInteractionContext context))
            {
                throw new InvalidOperationException(
                    $"Interaction op '{InteractionOpKinds.PushFrame}' references interaction context profile id {args.ContextProfileId} which is not installed.");
            }

            if (ctx.World.Has<ActiveInteractionContext>(ctx.Subject))
            {
                ctx.World.Get<ActiveInteractionContext>(ctx.Subject) = context;
                return;
            }

            ctx.World.Add(ctx.Subject, context);
        }

        private static void ExecutePopFrame(in InteractionOpContext ctx, in InteractionOpArgs args)
        {
            if (!ctx.World.TryGet<ActiveInteractionContext>(ctx.Subject, out ActiveInteractionContext mounted))
            {
                throw new InvalidOperationException(
                    $"Interaction op '{InteractionOpKinds.PopFrame}' executed on a subject with no active interaction context.");
            }

            // Only the op's own mounts are poppable — popping an exec-carried context or the
            // steady state is a configuration error and fails fast here.
            if (mounted.Source != ActiveInteractionContextSource.CastCommitOp)
            {
                throw new InvalidOperationException(
                    $"Interaction op '{InteractionOpKinds.PopFrame}' cannot remove the active interaction context on entity {ctx.Subject}; it was mounted by {mounted.Source}.");
            }

            ctx.World.Remove<ActiveInteractionContext>(ctx.Subject);
        }

        private static void ExecuteSubmitOrder(in InteractionOpContext ctx, in InteractionOpArgs args)
        {
            if (ctx.SubmitOrder == null)
            {
                throw new InvalidOperationException(
                    $"Interaction op '{InteractionOpKinds.SubmitOrder}' requires an order submit delegate on the op context.");
            }

            CastCommitOrderPayload payload = args.Payload;
            ctx.SubmitOrder(in ctx, in payload);
        }

        private CompiledProfile RequireInstalled(int profileId)
        {
            if (!IsInstalled(profileId))
            {
                throw new InvalidOperationException(
                    $"Cast commit profile id {profileId} ('{_profileIds.GetName(profileId)}') is not installed.");
            }

            return _profiles[profileId];
        }

        private void InstallProfile(CastCommitProfileDefinition definition)
        {
            int profileId = _profileIds.Register(definition.Id);
            if (profileId < _profiles.Length && _profiles[profileId] != null)
            {
                throw new InvalidOperationException($"Cast commit profile '{definition.Id}' is already installed.");
            }

            var payloadPool = new List<CastCommitPayloadEntry>();
            CompiledOp[] onActivate = CompileOps(definition.Id, definition.OnActivate, payloadPool);

            int actionCount = definition.FrameActions?.Count ?? 0;
            var actionIds = new int[actionCount];
            var actionOps = new CompiledOp[actionCount][];
            if (definition.FrameActions != null)
            {
                int index = 0;
                foreach (KeyValuePair<string, List<CastCommitOpDefinition>> action in definition.FrameActions)
                {
                    actionIds[index] = _actionIds.Register(action.Key);
                    actionOps[index] = CompileOps(definition.Id, action.Value, payloadPool);
                    index++;
                }
            }

            var profile = new CompiledProfile
            {
                OnActivate = onActivate,
                FrameActionIds = actionIds,
                FrameActionOps = actionOps,
                PayloadPool = payloadPool.ToArray(),
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

        private CompiledOp[] CompileOps(string profileId, List<CastCommitOpDefinition> ops, List<CastCommitPayloadEntry> payloadPool)
        {
            var compiled = new CompiledOp[ops.Count];
            for (int i = 0; i < ops.Count; i++)
            {
                CastCommitOpDefinition op = ops[i];
                if (!_opIndexByKind.TryGetValue(op.Op, out int opIndex))
                {
                    throw new InvalidOperationException(
                        $"Cast commit profile '{profileId}' references unknown interaction op '{op.Op}'.");
                }

                int contextProfileId = 0;
                if (!string.IsNullOrWhiteSpace(op.ContextProfileId))
                {
                    contextProfileId = _contextProfiles.ProfileIdRegistry.GetId(op.ContextProfileId);
                    if (!_contextProfiles.IsInstalled(contextProfileId))
                    {
                        throw new InvalidOperationException(
                            $"Cast commit profile '{profileId}' op '{op.Op}' references interaction context profile " +
                            $"'{op.ContextProfileId}' which is not installed.");
                    }
                }
                else if (opIndex == _pushFrameOpIndex)
                {
                    throw new InvalidOperationException(
                        $"Cast commit profile '{profileId}' op '{InteractionOpKinds.PushFrame}' requires contextProfileId.");
                }

                int payloadOffset = payloadPool.Count;
                int payloadCount = 0;
                if (op.Payload != null)
                {
                    foreach (KeyValuePair<string, string> entry in op.Payload)
                    {
                        int keyId = _payloadKeyIds.Register(entry.Key);
                        if (!_payloadValueSourceIds.TryGetId(entry.Value, out int valueSourceId))
                        {
                            throw new InvalidOperationException(
                                $"Cast commit profile '{profileId}' op '{op.Op}' payload '{entry.Key}' references " +
                                $"unknown value source '{entry.Value}'.");
                        }

                        payloadPool.Add(new CastCommitPayloadEntry(keyId, valueSourceId));
                        payloadCount++;
                    }
                }

                compiled[i] = new CompiledOp(opIndex, contextProfileId, payloadOffset, payloadCount);
            }

            return compiled;
        }

        /// <summary>Compiled op row: handler index plus resolved argument ids.</summary>
        private readonly struct CompiledOp
        {
            public CompiledOp(int opIndex, int contextProfileId, int payloadOffset, int payloadCount)
            {
                OpIndex = opIndex;
                ContextProfileId = contextProfileId;
                PayloadOffset = payloadOffset;
                PayloadCount = payloadCount;
            }

            public int OpIndex { get; }
            public int ContextProfileId { get; }
            public int PayloadOffset { get; }
            public int PayloadCount { get; }
        }

        private sealed class CompiledProfile
        {
            public CompiledOp[] OnActivate = Array.Empty<CompiledOp>();
            public int[] FrameActionIds = Array.Empty<int>();
            public CompiledOp[][] FrameActionOps = Array.Empty<CompiledOp[]>();
            public CastCommitPayloadEntry[] PayloadPool = Array.Empty<CastCommitPayloadEntry>();
        }
    }
}
