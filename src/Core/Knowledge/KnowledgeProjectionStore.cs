using System;
using Arch.Core;
using Ludots.Core.Association;

namespace Ludots.Core.Knowledge
{
    public sealed class KnowledgeProjectionStore
    {
        private readonly EntityKeyedSoaTable<KnowledgeProjectionPayload> _records;

        public KnowledgeProjectionStore(int initialCapacity = 64)
        {
            _records = new EntityKeyedSoaTable<KnowledgeProjectionPayload>(initialCapacity);
        }

        public int RecordCount => _records.ActiveCount;

        public int PhysicalRecordCount => _records.PhysicalSlotCount;

        public int RecordCapacity => _records.SlotCapacity;

        public uint Upsert(Entity viewer, Entity target, in KnowledgeDisclosureRecord record)
        {
            ValidateViewerAndTarget(viewer, target);

            EntityKeyedSoaKey key = EntityKeyedSoaKey.ForPair(viewer, target);
            var next = KnowledgeProjectionPayload.FromRecord(in record);
            bool changed = !_records.TryGet(key, int.MinValue, out KnowledgeProjectionPayload current, out _, out _) ||
                           current != next;

            return _records.Upsert(key, next, record.ExpiryTick, changed, out _);
        }

        public bool Remove(Entity viewer, Entity target)
        {
            if (viewer == Entity.Null || target == Entity.Null)
            {
                return false;
            }

            return _records.Remove(EntityKeyedSoaKey.ForPair(viewer, target));
        }

        public int Expire(int currentTick)
        {
            return _records.Expire(currentTick);
        }

        public int Compact()
        {
            return _records.Compact();
        }

        public bool TryGet(Entity viewer, Entity target, int currentTick, out KnowledgeDisclosureRecord record)
        {
            record = default;
            if (viewer == Entity.Null || target == Entity.Null)
            {
                return false;
            }

            if (!_records.TryGet(
                    EntityKeyedSoaKey.ForPair(viewer, target),
                    currentTick,
                    out KnowledgeProjectionPayload payload,
                    out uint revision,
                    out _))
            {
                return false;
            }

            record = payload.ToRecord(revision);
            return true;
        }

        public int CopyTargets(Entity viewer, int currentTick, Span<Entity> targets)
        {
            if (targets.IsEmpty || viewer == Entity.Null)
            {
                return 0;
            }

            return _records.CopySecondaryByPrimary(viewer, currentTick, targets);
        }

        public int CopyRecords(Entity viewer, int currentTick, Span<Entity> targets, Span<KnowledgeDisclosureRecord> records)
        {
            if (targets.IsEmpty || records.IsEmpty || viewer == Entity.Null)
            {
                return 0;
            }

            int limit = Math.Min(targets.Length, records.Length);
            Span<Entity> pageTargets = stackalloc Entity[64];
            Span<KnowledgeProjectionPayload> pagePayloads = stackalloc KnowledgeProjectionPayload[64];
            Span<uint> pageRevisions = stackalloc uint[64];
            int written = 0;
            while (written < limit)
            {
                int pageLimit = Math.Min(64, limit - written);
                int copied = _records.CopyPayloadsByPrimary(
                    viewer,
                    currentTick,
                    written,
                    pageTargets[..pageLimit],
                    pagePayloads[..pageLimit],
                    pageRevisions[..pageLimit]);
                if (copied == 0)
                {
                    break;
                }

                for (int i = 0; i < copied; i++)
                {
                    targets[written + i] = pageTargets[i];
                    records[written + i] = pagePayloads[i].ToRecord(pageRevisions[i]);
                }

                written += copied;
            }

            return written;
        }

        private static void ValidateViewerAndTarget(Entity viewer, Entity target)
        {
            if (viewer == Entity.Null)
            {
                throw new ArgumentException("Knowledge viewer entity is required.", nameof(viewer));
            }

            if (target == Entity.Null)
            {
                throw new ArgumentException("Knowledge target entity is required.", nameof(target));
            }
        }

        private readonly struct KnowledgeProjectionPayload : IEquatable<KnowledgeProjectionPayload>
        {
            private KnowledgeProjectionPayload(
                KnowledgePresence presence,
                KnowledgePositionAccess position,
                in KnowledgeIdMask256 attributeMask,
                in KnowledgeIdMask256 relationshipTypeMask,
                in KnowledgeIdMask256 tagMask,
                Entity source,
                int observedTick,
                int expiryTick,
                int confidencePermille)
            {
                Presence = presence;
                Position = position;
                AttributeMask = attributeMask;
                RelationshipTypeMask = relationshipTypeMask;
                TagMask = tagMask;
                Source = source;
                ObservedTick = observedTick;
                ExpiryTick = expiryTick;
                ConfidencePermille = confidencePermille;
            }

            public readonly KnowledgePresence Presence;
            public readonly KnowledgePositionAccess Position;
            public readonly KnowledgeIdMask256 AttributeMask;
            public readonly KnowledgeIdMask256 RelationshipTypeMask;
            public readonly KnowledgeIdMask256 TagMask;
            public readonly Entity Source;
            public readonly int ObservedTick;
            public readonly int ExpiryTick;
            public readonly int ConfidencePermille;

            public static KnowledgeProjectionPayload FromRecord(in KnowledgeDisclosureRecord record)
            {
                return new KnowledgeProjectionPayload(
                    record.Presence,
                    record.Position,
                    record.AttributeMask,
                    record.RelationshipTypeMask,
                    record.TagMask,
                    record.Source,
                    record.ObservedTick,
                    record.ExpiryTick,
                    record.ConfidencePermille);
            }

            public KnowledgeDisclosureRecord ToRecord(uint revision)
            {
                return new KnowledgeDisclosureRecord(
                    Presence,
                    Position,
                    AttributeMask,
                    RelationshipTypeMask,
                    TagMask,
                    Source,
                    ObservedTick,
                    ExpiryTick,
                    ConfidencePermille,
                    revision);
            }

            public bool Equals(KnowledgeProjectionPayload other)
            {
                return Presence == other.Presence &&
                       Position == other.Position &&
                       AttributeMask == other.AttributeMask &&
                       RelationshipTypeMask == other.RelationshipTypeMask &&
                       TagMask == other.TagMask &&
                       Source == other.Source &&
                       ObservedTick == other.ObservedTick &&
                       ExpiryTick == other.ExpiryTick &&
                       ConfidencePermille == other.ConfidencePermille;
            }

            public override bool Equals(object? obj)
            {
                return obj is KnowledgeProjectionPayload other && Equals(other);
            }

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(Presence);
                hash.Add(Position);
                hash.Add(AttributeMask);
                hash.Add(RelationshipTypeMask);
                hash.Add(TagMask);
                hash.Add(Source);
                hash.Add(ObservedTick);
                hash.Add(ExpiryTick);
                hash.Add(ConfidencePermille);
                return hash.ToHashCode();
            }

            public static bool operator ==(KnowledgeProjectionPayload left, KnowledgeProjectionPayload right) => left.Equals(right);

            public static bool operator !=(KnowledgeProjectionPayload left, KnowledgeProjectionPayload right) => !left.Equals(right);
        }
    }
}
