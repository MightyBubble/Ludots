using Arch.Core;
using Ludots.Core.Knowledge;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Presenter-facing knowledge facts. Producers resolve knowledge; presenters only consume facts.
    /// </summary>
    public readonly struct PerformProjectionFacts
    {
        public readonly bool HasProjection;
        public readonly Entity Source;
        public readonly KnowledgePresence Presence;
        public readonly KnowledgePositionAccess Position;
        public readonly KnowledgeIdMask256 AttributeMask;
        public readonly KnowledgeIdMask256 RelationshipTypeMask;
        public readonly KnowledgeIdMask256 TagMask;
        public readonly int ObservedTick;
        public readonly int ExpiryTick;
        public readonly int ConfidencePermille;
        public readonly uint Revision;

        public PerformProjectionFacts(in KnowledgeProjection projection)
        {
            HasProjection = projection.CanKnowEntity;
            Source = projection.Source;
            Presence = projection.Presence;
            Position = projection.Position;
            AttributeMask = projection.AttributeMask;
            RelationshipTypeMask = projection.RelationshipTypeMask;
            TagMask = projection.TagMask;
            ObservedTick = projection.ObservedTick;
            ExpiryTick = projection.ExpiryTick;
            ConfidencePermille = projection.ConfidencePermille;
            Revision = projection.Revision;
        }

        public bool CanKnowEntity => HasProjection && Presence != KnowledgePresence.Unknown;

        public bool CanReadPosition(KnowledgePositionAccess required)
        {
            return CanKnowEntity && (required == KnowledgePositionAccess.None || Position >= required);
        }

        public bool CanReadAttributes(ReadOnlySpan<int> attributeIds)
        {
            if (!CanKnowEntity)
            {
                return false;
            }

            for (int i = 0; i < attributeIds.Length; i++)
            {
                int attributeId = attributeIds[i];
                if ((uint)attributeId >= 256u)
                {
                    return false;
                }

                if (attributeId >= 0 && !AttributeMask.ContainsId(attributeId))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
