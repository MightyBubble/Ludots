using System;
using Arch.Core;

namespace Ludots.Core.Knowledge
{
    public enum KnowledgePresence : byte
    {
        Unknown = 0,
        Known = 1,
        LiveVisible = 2,
        HiddenWithSource = 3,
    }

    public enum KnowledgePositionAccess : byte
    {
        None = 0,
        LastKnown = 1,
        Live = 2,
    }

    public readonly struct KnowledgeDisclosureRecord
    {
        public readonly KnowledgePresence Presence;
        public readonly KnowledgePositionAccess Position;
        public readonly KnowledgeIdMask256 AttributeMask;
        public readonly KnowledgeIdMask256 RelationshipTypeMask;
        public readonly KnowledgeIdMask256 TagMask;
        public readonly Entity Source;
        public readonly int ObservedTick;
        public readonly int ExpiryTick;
        public readonly int ConfidencePermille;
        public readonly uint Revision;

        public KnowledgeDisclosureRecord(
            KnowledgePresence presence,
            KnowledgePositionAccess position,
            in KnowledgeIdMask256 attributeMask,
            in KnowledgeIdMask256 relationshipTypeMask,
            in KnowledgeIdMask256 tagMask,
            Entity source,
            int observedTick,
            int expiryTick,
            int confidencePermille,
            uint revision)
        {
            ValidatePresence(presence);
            ValidatePositionAccess(position);
            if ((uint)confidencePermille > 1000u)
            {
                throw new ArgumentOutOfRangeException(nameof(confidencePermille), "Knowledge confidence must be in permille range 0..1000.");
            }

            Presence = presence;
            Position = position;
            AttributeMask = attributeMask;
            RelationshipTypeMask = relationshipTypeMask;
            TagMask = tagMask;
            Source = source;
            ObservedTick = observedTick;
            ExpiryTick = expiryTick;
            ConfidencePermille = confidencePermille;
            Revision = revision;
        }

        public bool IsExpired(int currentTick)
        {
            return ExpiryTick > 0 && currentTick >= ExpiryTick;
        }

        private static void ValidatePresence(KnowledgePresence presence)
        {
            if ((uint)presence > (uint)KnowledgePresence.HiddenWithSource)
            {
                throw new ArgumentOutOfRangeException(nameof(presence), "Unknown knowledge presence value.");
            }
        }

        private static void ValidatePositionAccess(KnowledgePositionAccess position)
        {
            if ((uint)position > (uint)KnowledgePositionAccess.Live)
            {
                throw new ArgumentOutOfRangeException(nameof(position), "Unknown knowledge position access value.");
            }
        }
    }
}
