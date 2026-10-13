using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    /// <summary>
    /// A panel click never submits an order itself: it queues one slot activation per distinct
    /// slot index, and <see cref="Dispatch"/> (EventDispatch phase) publishes the acting members
    /// into <see cref="MembersCollectionKey"/> on the owner and fires <see cref="EventName"/>
    /// with the slot in <see cref="SlotPayloadKey"/>. The active interaction context's graph
    /// decides what the click means (cast now, open an aiming context, ...).
    /// </summary>
    public sealed class EntityCommandPanelSlotActivations
    {
        public const string EventName = "EntityCommandPanel.ActivateSlot";
        public const string SlotPayloadKey = "Panel.Slot";
        public const string MembersCollectionKey = "entity_command_panel.activation.members";

        private readonly GameEngine _engine;
        private readonly PendingActivation[] _pending;
        private readonly Entity[] _members;
        private int _pendingCount;
        private int _memberCount;

        public EntityCommandPanelSlotActivations(GameEngine engine, int maxPendingActivations, int maxPendingMembers)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            if (maxPendingActivations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPendingActivations));
            }

            if (maxPendingMembers < maxPendingActivations)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPendingMembers));
            }

            _pending = new PendingActivation[maxPendingActivations];
            _members = new Entity[maxPendingMembers];
        }

        public int PendingCount => _pendingCount;

        public EntityCommandPanelActivationResult Enqueue(Entity owner, int slotIndex, ReadOnlySpan<Entity> members)
        {
            Entity representative = members.IsEmpty ? owner : members[0];
            if (!_engine.World.IsAlive(owner) || members.IsEmpty || slotIndex < 0)
            {
                return EntityCommandPanelActivationResult.Rejected(representative, EntityCommandPanelActivationRejection.InvalidActor);
            }

            if (_engine.CurrentMapSession is not { } session ||
                !_engine.TriggerManager.HasDispatchTarget(session.MapId, EventName))
            {
                return EntityCommandPanelActivationResult.Rejected(representative, EntityCommandPanelActivationRejection.NoHandler);
            }

            if (_pendingCount >= _pending.Length || _memberCount + members.Length > _members.Length)
            {
                return EntityCommandPanelActivationResult.Rejected(representative, EntityCommandPanelActivationRejection.QueueFull);
            }

            members.CopyTo(_members.AsSpan(_memberCount));
            _pending[_pendingCount++] = new PendingActivation(owner, slotIndex, _memberCount, members.Length);
            _memberCount += members.Length;
            return EntityCommandPanelActivationResult.Accepted(representative);
        }

        public int Dispatch()
        {
            if (_pendingCount == 0)
            {
                return 0;
            }

            if (_engine.CurrentMapSession is not { } session)
            {
                Clear();
                return 0;
            }

            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("Entity command panel activations require EntityCollectionStore.");
            CustomEventNameRegistry customEvents = _engine.GetService(CoreServiceKeys.CustomEventNameRegistry)
                ?? throw new InvalidOperationException("Entity command panel activations require CustomEventNameRegistry.");
            MapId mapId = session.MapId;
            int dispatched = 0;
            for (int i = 0; i < _pendingCount; i++)
            {
                PendingActivation activation = _pending[i];
                if (!_engine.World.IsAlive(activation.Owner))
                {
                    continue;
                }

                ReadOnlySpan<Entity> members = _members.AsSpan(activation.MemberOffset, activation.MemberCount);
                var descriptor = EntityCollectionDescriptor.Create(
                    MembersCollectionKey,
                    EntityCollectionSourceKind.Explicit,
                    EntityCollectionRoleKind.Display,
                    activation.Owner,
                    members[0],
                    "Entity command panel activation",
                    "Members acting on the activated panel slot.");
                collections.Replace(activation.Owner, descriptor, members, activation.Owner);

                ScriptContext context = _engine.CreateContext();
                context.Set(SlotPayloadKey, activation.SlotIndex);
                _engine.TriggerManager.FireMapCustomEvent(mapId, EventName, context, customEvents);
                dispatched++;
            }

            Clear();
            return dispatched;
        }

        private void Clear()
        {
            _pendingCount = 0;
            _memberCount = 0;
        }

        private readonly record struct PendingActivation(Entity Owner, int SlotIndex, int MemberOffset, int MemberCount);
    }
}
