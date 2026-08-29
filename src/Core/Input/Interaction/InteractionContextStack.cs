using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>Reserved interaction context ids owned by the engine.</summary>
    public static class InteractionContextIds
    {
        /// <summary>Bottom frame pushed by the engine at startup; never removable.</summary>
        public const string Default = "interaction.context.default";
    }

    /// <summary>
    /// String-keyed frame description. Strings are resolved to int ids by
    /// <see cref="InteractionContextStack.Push(in InteractionContextFrameDescriptor)"/>.
    /// </summary>
    public readonly record struct InteractionContextFrameDescriptor(
        string ContextId,
        string ActiveCollectionKey,
        string ActiveEntityViewKey,
        Entity ContextEntity,
        string FilterProfileId,
        string CommandIntentProfileId,
        string InputContextId)
    {
        /// <summary>Create a validated descriptor. Profile and input context ids are optional.</summary>
        public static InteractionContextFrameDescriptor Create(
            string contextId,
            string activeCollectionKey,
            string activeEntityViewKey,
            Entity contextEntity = default,
            string filterProfileId = "",
            string commandIntentProfileId = "",
            string inputContextId = "")
        {
            if (string.IsNullOrWhiteSpace(contextId))
            {
                throw new ArgumentException("Interaction context id is required.", nameof(contextId));
            }

            if (string.IsNullOrWhiteSpace(activeCollectionKey))
            {
                throw new ArgumentException("Active collection key is required.", nameof(activeCollectionKey));
            }

            if (string.IsNullOrWhiteSpace(activeEntityViewKey))
            {
                throw new ArgumentException("Active entity view key is required.", nameof(activeEntityViewKey));
            }

            return new InteractionContextFrameDescriptor(
                contextId.Trim(),
                activeCollectionKey.Trim(),
                activeEntityViewKey.Trim(),
                contextEntity,
                filterProfileId ?? string.Empty,
                commandIntentProfileId ?? string.Empty,
                inputContextId ?? string.Empty);
        }
    }

    /// <summary>
    /// Resolved interaction context frame. Carries opaque int ids and the owning token only;
    /// entity membership lives in <c>EntityCollectionStore</c>, never in the frame.
    /// </summary>
    public readonly struct InteractionContextFrame
    {
        /// <summary>Registered context id.</summary>
        public readonly int ContextId;

        /// <summary>Registered entity collection key id the frame reads/writes.</summary>
        public readonly int ActiveCollectionKeyId;

        /// <summary>Registered entity view key id the frame exposes.</summary>
        public readonly int ActiveEntityViewKeyId;

        /// <summary>Optional owning entity (e.g. ability exec instance); default allowed.</summary>
        public readonly Entity ContextEntity;

        /// <summary>Registered filter profile id; 0 = undeclared.</summary>
        public readonly int FilterProfileId;

        /// <summary>Registered command intent profile id; 0 = undeclared.</summary>
        public readonly int CommandIntentProfileId;

        /// <summary>Registered IMC input context id; 0 = undeclared.</summary>
        public readonly int InputContextId;

        /// <summary>Token identifying the frame owner; removal is token-addressed.</summary>
        public readonly long OwnerToken;

        internal InteractionContextFrame(
            int contextId,
            int activeCollectionKeyId,
            int activeEntityViewKeyId,
            Entity contextEntity,
            int filterProfileId,
            int commandIntentProfileId,
            int inputContextId,
            long ownerToken)
        {
            ContextId = contextId;
            ActiveCollectionKeyId = activeCollectionKeyId;
            ActiveEntityViewKeyId = activeEntityViewKeyId;
            ContextEntity = contextEntity;
            FilterProfileId = filterProfileId;
            CommandIntentProfileId = commandIntentProfileId;
            InputContextId = inputContextId;
            OwnerToken = ownerToken;
        }
    }

    /// <summary>
    /// Local-client interaction context stack (RFC-0065 CTX-1). Frames are token-owned and
    /// removable from any position; the top frame is the last activated context. The stack is
    /// the interaction state machine only — translating its frames into input handler contexts
    /// is <c>InputContextProjectionSystem</c>'s job, and nothing here touches input handling.
    /// </summary>
    public sealed class InteractionContextStack
    {
        private readonly StringIntRegistry _collectionKeyRegistry;
        private readonly StringIntRegistry _contextIdRegistry;
        private readonly StringIntRegistry _entityViewKeyRegistry;
        private readonly StringIntRegistry _filterProfileIdRegistry;
        private readonly StringIntRegistry _commandIntentProfileIdRegistry;
        private readonly StringIntRegistry _inputContextIdRegistry;
        private readonly int _defaultContextId;

        private int[] _contextIds;
        private int[] _collectionKeyIds;
        private int[] _viewKeyIds;
        private Entity[] _contextEntities;
        private int[] _filterProfileIds;
        private int[] _commandIntentProfileIds;
        private int[] _inputContextIds;
        private long[] _ownerTokens;
        private int _count;
        private long _nextToken = 1;

        public InteractionContextStack(StringIntRegistry collectionKeyRegistry, int initialCapacity = 8)
        {
            _collectionKeyRegistry = collectionKeyRegistry ?? throw new ArgumentNullException(nameof(collectionKeyRegistry));
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _contextIdRegistry = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _entityViewKeyRegistry = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _filterProfileIdRegistry = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _commandIntentProfileIdRegistry = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _inputContextIdRegistry = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _defaultContextId = _contextIdRegistry.Register(InteractionContextIds.Default);

            _contextIds = new int[initialCapacity];
            _collectionKeyIds = new int[initialCapacity];
            _viewKeyIds = new int[initialCapacity];
            _contextEntities = new Entity[initialCapacity];
            _filterProfileIds = new int[initialCapacity];
            _commandIntentProfileIds = new int[initialCapacity];
            _inputContextIds = new int[initialCapacity];
            _ownerTokens = new long[initialCapacity];
        }

        /// <summary>Collection key id space shared with <c>EntityCollectionStore</c>.</summary>
        public StringIntRegistry CollectionKeyRegistry => _collectionKeyRegistry;

        /// <summary>Context id registry.</summary>
        public StringIntRegistry ContextIdRegistry => _contextIdRegistry;

        /// <summary>Entity view key registry.</summary>
        public StringIntRegistry EntityViewKeyRegistry => _entityViewKeyRegistry;

        /// <summary>Filter profile id registry.</summary>
        public StringIntRegistry FilterProfileIdRegistry => _filterProfileIdRegistry;

        /// <summary>Command intent profile id registry.</summary>
        public StringIntRegistry CommandIntentProfileIdRegistry => _commandIntentProfileIdRegistry;

        /// <summary>IMC input context id registry; reverse lookup feeds the input context projection.</summary>
        public StringIntRegistry InputContextIdRegistry => _inputContextIdRegistry;

        /// <summary>Number of frames on the stack.</summary>
        public int Count => _count;

        /// <summary>Bumped on every stack mutation.</summary>
        public uint Revision { get; private set; }

        /// <summary>Push a frame; returns the owner token addressing it.</summary>
        public long Push(in InteractionContextFrameDescriptor descriptor)
        {
            return Push(in descriptor, out _);
        }

        /// <summary>Push a frame; returns the owner token and the resolved frame.</summary>
        public long Push(in InteractionContextFrameDescriptor descriptor, out InteractionContextFrame frame)
        {
            if (string.IsNullOrWhiteSpace(descriptor.ContextId))
            {
                throw new ArgumentException("Interaction context id is required.", nameof(descriptor));
            }

            if (string.IsNullOrWhiteSpace(descriptor.ActiveCollectionKey))
            {
                throw new ArgumentException("Active collection key is required.", nameof(descriptor));
            }

            if (string.IsNullOrWhiteSpace(descriptor.ActiveEntityViewKey))
            {
                throw new ArgumentException("Active entity view key is required.", nameof(descriptor));
            }

            EnsureCapacity(_count + 1);
            long token = _nextToken++;
            int index = _count;
            _contextIds[index] = _contextIdRegistry.Register(descriptor.ContextId);
            _collectionKeyIds[index] = _collectionKeyRegistry.Register(descriptor.ActiveCollectionKey);
            _viewKeyIds[index] = _entityViewKeyRegistry.Register(descriptor.ActiveEntityViewKey);
            _contextEntities[index] = descriptor.ContextEntity;
            _filterProfileIds[index] = RegisterOptional(_filterProfileIdRegistry, descriptor.FilterProfileId);
            _commandIntentProfileIds[index] = RegisterOptional(_commandIntentProfileIdRegistry, descriptor.CommandIntentProfileId);
            _inputContextIds[index] = RegisterOptional(_inputContextIdRegistry, descriptor.InputContextId);
            _ownerTokens[index] = token;
            _count++;
            Revision++;

            frame = FrameAt(index);
            return token;
        }

        /// <summary>
        /// Remove the frame owned by <paramref name="ownerToken"/> from any stack position.
        /// Throws <see cref="InvalidOperationException"/> when the frame is the reserved default frame.
        /// </summary>
        public bool RemoveByToken(long ownerToken)
        {
            for (int index = _count - 1; index >= 0; index--)
            {
                if (_ownerTokens[index] != ownerToken)
                {
                    continue;
                }

                if (_contextIds[index] == _defaultContextId)
                {
                    throw new InvalidOperationException(
                        $"Interaction context frame '{InteractionContextIds.Default}' is reserved and cannot be removed.");
                }

                RemoveAt(index);
                Revision++;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Remove all frames owned by <paramref name="contextEntity"/> (lifecycle reclamation).
        /// Returns the number of removed frames.
        /// </summary>
        public int RemoveByContextEntity(Entity contextEntity)
        {
            if (contextEntity == default)
            {
                return 0;
            }

            int removedCount = 0;
            for (int index = _count - 1; index >= 0; index--)
            {
                if (_contextEntities[index] != contextEntity || _contextIds[index] == _defaultContextId)
                {
                    continue;
                }

                RemoveAt(index);
                Revision++;
                removedCount++;
            }

            return removedCount;
        }

        /// <summary>Get the top frame (last activated context).</summary>
        public bool TryPeek(out InteractionContextFrame frame)
        {
            if (_count == 0)
            {
                frame = default;
                return false;
            }

            frame = FrameAt(_count - 1);
            return true;
        }

        /// <summary>Get the frame at <paramref name="index"/>, bottom-up (0 = bottom).</summary>
        public bool TryGetAt(int index, out InteractionContextFrame frame)
        {
            if ((uint)index >= (uint)_count)
            {
                frame = default;
                return false;
            }

            frame = FrameAt(index);
            return true;
        }

        private static int RegisterOptional(StringIntRegistry registry, string value)
        {
            return string.IsNullOrWhiteSpace(value) ? registry.InvalidId : registry.Register(value.Trim());
        }

        private InteractionContextFrame FrameAt(int index)
        {
            return new InteractionContextFrame(
                _contextIds[index],
                _collectionKeyIds[index],
                _viewKeyIds[index],
                _contextEntities[index],
                _filterProfileIds[index],
                _commandIntentProfileIds[index],
                _inputContextIds[index],
                _ownerTokens[index]);
        }

        private void RemoveAt(int index)
        {
            int tail = _count - index - 1;
            if (tail > 0)
            {
                Array.Copy(_contextIds, index + 1, _contextIds, index, tail);
                Array.Copy(_collectionKeyIds, index + 1, _collectionKeyIds, index, tail);
                Array.Copy(_viewKeyIds, index + 1, _viewKeyIds, index, tail);
                Array.Copy(_contextEntities, index + 1, _contextEntities, index, tail);
                Array.Copy(_filterProfileIds, index + 1, _filterProfileIds, index, tail);
                Array.Copy(_commandIntentProfileIds, index + 1, _commandIntentProfileIds, index, tail);
                Array.Copy(_inputContextIds, index + 1, _inputContextIds, index, tail);
                Array.Copy(_ownerTokens, index + 1, _ownerTokens, index, tail);
            }

            _count--;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _contextIds.Length)
            {
                return;
            }

            int newLength = Math.Max(_contextIds.Length * 2, required);
            Array.Resize(ref _contextIds, newLength);
            Array.Resize(ref _collectionKeyIds, newLength);
            Array.Resize(ref _viewKeyIds, newLength);
            Array.Resize(ref _contextEntities, newLength);
            Array.Resize(ref _filterProfileIds, newLength);
            Array.Resize(ref _commandIntentProfileIds, newLength);
            Array.Resize(ref _inputContextIds, newLength);
            Array.Resize(ref _ownerTokens, newLength);
        }
    }
}
