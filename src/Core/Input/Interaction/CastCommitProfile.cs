using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Built-in interaction op kinds (RFC-0065 §5.5, DEC-11/DEC-13). Registry keys, not a closed
    /// enum — infrastructure primitives, never casting semantics.
    /// </summary>
    public static class InteractionOpKinds
    {
        /// <summary>Push the frame declared by the op's <c>contextProfileId</c> onto the context stack.</summary>
        public const string PushFrame = "pushFrame";

        /// <summary>Remove the top context stack frame (token addressed; the default frame is irremovable).</summary>
        public const string PopFrame = "popFrame";

        /// <summary>Invoke the order submit delegate with the op's payload value sources.</summary>
        public const string SubmitOrder = "submitOrder";
    }

    /// <summary>
    /// Built-in payload value source names (RFC-0065 §5.5, DEC-11). Registry keys resolved at
    /// install; the value-producing delegates are bound by order wiring, not by this kernel.
    /// </summary>
    public static class CastCommitPayloadValueSources
    {
        /// <summary>The pointer's current world position at op execution time.</summary>
        public const string CursorWorld = "cursorWorld";

        /// <summary>The pointer position captured through the active targeting frame.</summary>
        public const string FramePointer = "framePointer";
    }

    /// <summary>One compiled payload entry: an order argument slot key and a value source, both registry ids.</summary>
    public readonly record struct CastCommitPayloadEntry(int KeyId, int ValueSourceId);

    /// <summary>
    /// Allocation-free view over a compiled op's payload entries
    /// (slices the owning profile's payload pool).
    /// </summary>
    public readonly struct CastCommitOrderPayload
    {
        private readonly CastCommitPayloadEntry[] _pool;
        private readonly int _offset;

        internal CastCommitOrderPayload(CastCommitPayloadEntry[] pool, int offset, int count)
        {
            _pool = pool;
            _offset = offset;
            Count = count;
        }

        /// <summary>Number of payload entries on the op.</summary>
        public int Count { get; }

        /// <summary>Payload entry at <paramref name="index"/>.</summary>
        public CastCommitPayloadEntry this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _pool[_offset + index];
            }
        }
    }

    /// <summary>
    /// Order submit callback (bound by order wiring; stubbed in tests). Receives the executing
    /// context and the op's payload value sources; the real implementation resolves sources through
    /// <see cref="InteractionOpContext.ValueResolver"/> and builds the order.
    /// </summary>
    public delegate void CastCommitOrderSubmit(in InteractionOpContext ctx, in CastCommitOrderPayload payload);

    /// <summary>
    /// Payload value source resolver (bound by order wiring): resolves a (payload key, value source)
    /// pair to a world-cm point at execution time. Returns false when the source has no value.
    /// </summary>
    public delegate bool CastCommitPayloadValueResolver(int payloadKeyId, int valueSourceId, out Vector3 worldCm);

    /// <summary>
    /// Execution context handed to every interaction op: the client context stack, the order submit
    /// delegate, the payload value resolver, and the owning entity for pushed frames (default for
    /// client-initiated pushes; the exec carrier for sim-driven ones).
    /// </summary>
    public readonly struct InteractionOpContext
    {
        public InteractionOpContext(
            InteractionContextStack stack,
            CastCommitOrderSubmit submitOrder = null,
            CastCommitPayloadValueResolver valueResolver = null,
            Entity contextEntity = default)
        {
            Stack = stack ?? throw new ArgumentNullException(nameof(stack));
            SubmitOrder = submitOrder;
            ValueResolver = valueResolver;
            ContextEntity = contextEntity;
        }

        /// <summary>Client interaction context stack the frame ops act on.</summary>
        public InteractionContextStack Stack { get; }

        /// <summary>Order submit delegate; required before a <c>submitOrder</c> op executes.</summary>
        public CastCommitOrderSubmit SubmitOrder { get; }

        /// <summary>Payload value source resolver; consumed by the submit delegate.</summary>
        public CastCommitPayloadValueResolver ValueResolver { get; }

        /// <summary>Owner entity recorded on frames pushed by <c>pushFrame</c>.</summary>
        public Entity ContextEntity { get; }
    }

    /// <summary>Compiled arguments of one op invocation: the resolved context profile id and the payload view.</summary>
    public readonly struct InteractionOpArgs
    {
        internal InteractionOpArgs(int contextProfileId, in CastCommitOrderPayload payload)
        {
            ContextProfileId = contextProfileId;
            Payload = payload;
        }

        /// <summary>Installed interaction context profile id; 0 when the op declared none.</summary>
        public int ContextProfileId { get; }

        /// <summary>The op's payload value sources.</summary>
        public CastCommitOrderPayload Payload { get; }
    }

    /// <summary>
    /// Interaction op executor (DEC-11 registry entry). Must be steady-state allocation free.
    /// </summary>
    public delegate void InteractionOpHandler(in InteractionOpContext ctx, in InteractionOpArgs args);

    /// <summary>Merged root of <c>Input/cast_commit_profiles.json</c>.</summary>
    public sealed class CastCommitProfilesConfig
    {
        public List<CastCommitProfileDefinition> Profiles { get; set; }
    }

    /// <summary>
    /// One cast commit profile (RFC-0065 §5.5, DEC-13): the op sequence executed on slot activation
    /// and, when a targeting frame was pushed, the frame's action → op sequence table. There is no
    /// FSM schema — the loader rejects any key beyond these three.
    /// </summary>
    public sealed class CastCommitProfileDefinition
    {
        public string Id { get; set; } = string.Empty;
        public List<CastCommitOpDefinition> OnActivate { get; set; }

        /// <summary>Input action id (mod data) → op sequence, active while the pushed frame is on top.</summary>
        public Dictionary<string, List<CastCommitOpDefinition>> FrameActions { get; set; }
    }

    /// <summary>One op declaration: a registry kind, an optional payload map, and an optional context profile reference.</summary>
    public sealed class CastCommitOpDefinition
    {
        public string Op { get; set; } = string.Empty;

        /// <summary>Order argument slot key → payload value source name.</summary>
        public Dictionary<string, string> Payload { get; set; }

        /// <summary>Interaction context profile pushed by <c>pushFrame</c>-style ops.</summary>
        public string ContextProfileId { get; set; }
    }
}
