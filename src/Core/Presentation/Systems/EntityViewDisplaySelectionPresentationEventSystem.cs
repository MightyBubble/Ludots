using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.EntityView;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Mirrors EntityView display collection diffs into legacy selection presentation events
    /// so performer rules keyed on <c>selection.live.primary</c> keep working without
    /// dual-writing <see cref="SelectionRuntime"/>.
    /// </summary>
    public sealed class EntityViewDisplaySelectionPresentationEventSystem : BaseSystem<World, float>
    {
        private const int InitialMemberCapacity = 128;

        private readonly Dictionary<string, object> _globals;
        private readonly EntityCollectionStore _collections;
        private readonly PresentationEventStream _events;
        private readonly int _livePrimarySetKeyId;
        private readonly HashSet<Entity> _currentMembers = new();
        private readonly HashSet<Entity> _previousMembers = new();
        private Entity[] _copyBuffer = Array.Empty<Entity>();
        private uint _trackedRevision;
        private ulong _trackedSignature;
        private Entity _trackedOwner;
        private int _trackedDisplayKeyId;
        private bool _hasSnapshot;

        public EntityViewDisplaySelectionPresentationEventSystem(
            World world,
            Dictionary<string, object> globals,
            EntityCollectionStore collections,
            PresentationEventStream events,
            SelectionRuntime selectionRuntime)
            : base(world)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            ArgumentNullException.ThrowIfNull(selectionRuntime);
            if (!selectionRuntime.TryGetSetKeyId(SelectionSetKeys.LivePrimary, out _livePrimarySetKeyId))
            {
                throw new InvalidOperationException(
                    $"EntityView display selection bridge requires registered set key '{SelectionSetKeys.LivePrimary}'.");
            }
        }

        public override void Update(in float dt)
        {
            if (!EntityViewRuntime.TryGetCurrentViewer(World, _globals, out Entity viewer) ||
                !EntityViewRuntime.TryResolveCurrentProfile(_globals, RequireEntityViewConfig(), out EntityViewProfileEntry profile) ||
                !_collections.TryGet(viewer, profile.DisplayCollectionKey, out EntityCollectionHandle handle) ||
                !_collections.TryGetView(handle, out EntityCollectionView view))
            {
                PublishClear();
                return;
            }

            if (_hasSnapshot &&
                _trackedOwner == viewer &&
                _trackedDisplayKeyId == view.KeyId &&
                _trackedRevision == view.Revision &&
                _trackedSignature == view.Signature)
            {
                return;
            }

            PublishDiff(viewer, in view, handle);
        }

        private void PublishDiff(Entity viewer, in EntityCollectionView view, EntityCollectionHandle handle)
        {
            EnsureCopyCapacity(view.Count);
            int written = view.Count == 0
                ? 0
                : _collections.CopyEntities(handle, 0, _copyBuffer.AsSpan(0, view.Count));

            _currentMembers.Clear();
            _currentMembers.EnsureCapacity(written);
            for (int i = 0; i < written; i++)
            {
                Entity member = _copyBuffer[i];
                if (member != Entity.Null && World.IsAlive(member))
                {
                    _currentMembers.Add(member);
                }
            }

            if (_hasSnapshot)
            {
                foreach (Entity oldMember in _previousMembers)
                {
                    if (!_currentMembers.Contains(oldMember))
                    {
                        PublishOne(PresentationEventKind.SelectionMemberRemoved, oldMember, viewer);
                    }
                }
            }

            foreach (Entity member in _currentMembers)
            {
                if (!_hasSnapshot || !_previousMembers.Contains(member))
                {
                    PublishOne(PresentationEventKind.SelectionMemberAdded, member, viewer);
                }
            }

            _previousMembers.Clear();
            foreach (Entity member in _currentMembers)
            {
                _previousMembers.Add(member);
            }

            _trackedOwner = viewer;
            _trackedDisplayKeyId = view.KeyId;
            _trackedRevision = view.Revision;
            _trackedSignature = view.Signature;
            _hasSnapshot = true;
        }

        private void PublishClear()
        {
            if (!_hasSnapshot)
            {
                return;
            }

            foreach (Entity oldMember in _previousMembers)
            {
                PublishOne(PresentationEventKind.SelectionMemberRemoved, oldMember, _trackedOwner);
            }

            _previousMembers.Clear();
            _currentMembers.Clear();
            _hasSnapshot = false;
            _trackedOwner = default;
            _trackedDisplayKeyId = 0;
            _trackedRevision = 0;
            _trackedSignature = 0;
        }

        private void PublishOne(PresentationEventKind kind, Entity member, Entity container)
        {
            if (!World.IsAlive(member))
            {
                return;
            }

            int stableId = World.Has<PresentationStableId>(member)
                ? World.Get<PresentationStableId>(member).Value
                : 0;

            if (!_events.TryAdd(new PresentationEvent
            {
                Kind = kind,
                KeyId = _livePrimarySetKeyId,
                Source = member,
                Target = container,
                PayloadA = stableId,
            }))
            {
                throw new InvalidOperationException(
                    "PresentationEventStream is full while publishing EntityView display selection bridge events.");
            }
        }

        private EntityViewRuntimeConfig RequireEntityViewConfig()
        {
            if (_globals.TryGetValue(CoreServiceKeys.EntityViewConfig.Name, out object? configObj) &&
                configObj is EntityViewRuntimeConfig config)
            {
                return config;
            }

            throw new InvalidOperationException(
                $"{nameof(EntityViewDisplaySelectionPresentationEventSystem)} requires {CoreServiceKeys.EntityViewConfig.Name}.");
        }

        private void EnsureCopyCapacity(int count)
        {
            if (_copyBuffer.Length >= count)
            {
                return;
            }

            int next = Math.Max(count, _copyBuffer.Length == 0 ? InitialMemberCapacity : _copyBuffer.Length * 2);
            Array.Resize(ref _copyBuffer, next);
        }
    }
}
