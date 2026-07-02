using System;
using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Vision
{
    public sealed class FogKnowledgeProjector
    {
        private readonly KnowledgeProjectionStore _knowledge;
        private readonly ConcealmentRule _concealment;

        public FogKnowledgeProjector(
            KnowledgeProjectionStore knowledge,
            IFogConcealmentSource? concealment = null)
        {
            _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
            _concealment = new ConcealmentRule(concealment);
        }

        public int Project(
            Entity viewer,
            Entity source,
            WorldCmInt2 viewerPosition,
            FogField field,
            ReadOnlySpan<FogOccupant> occupants,
            in FogProjectionPolicy policy,
            int currentTick,
            byte detectionStrength = 0)
        {
            if (viewer == Entity.Null)
            {
                throw new ArgumentException("Fog projection viewer is required.", nameof(viewer));
            }

            if (source == Entity.Null)
            {
                throw new ArgumentException("Fog projection source is required.", nameof(source));
            }

            FogCell viewerCell = field.WorldToCell(viewerPosition);
            int projected = 0;
            uint layerMask = LayerMaskFromField(field);
            bool trueSightActive = detectionStrength > 0 && policy.TrueSightRevealsConcealment;

            for (int i = 0; i < occupants.Length; i++)
            {
                FogOccupant occupant = occupants[i];
                bool exposesOnLayer = (occupant.ExposeLayerMask & layerMask) != 0u;
                if (!exposesOnLayer)
                {
                    continue;
                }

                FogCell occupantCell = field.WorldToCell(occupant.Position);
                CellVisibility visibility = field.GetVisibility(occupantCell);
                KnowledgeDisclosureRecord record;
                if (visibility == CellVisibility.Visible && detectionStrength >= occupant.StealthLevel)
                {
                    if (!_concealment.Allows(viewerCell, occupantCell, trueSightActive, in policy))
                    {
                        record = CreateHidden(source, currentTick);
                    }
                    else
                    {
                        record = CreateLive(source, in policy, currentTick);
                    }
                }
                else if (visibility == CellVisibility.Visible && occupant.StealthLevel > detectionStrength)
                {
                    record = CreateHidden(source, currentTick);
                }
                else if (visibility == CellVisibility.Denied)
                {
                    record = CreateHidden(source, currentTick);
                }
                else if (visibility == CellVisibility.Explored)
                {
                    record = CreateKnown(source, in policy, currentTick);
                }
                else
                {
                    continue;
                }

                _knowledge.Upsert(viewer, occupant.Entity, in record);
                projected++;
            }

            return projected;
        }

        public void Decay(
            Entity viewer,
            Entity target,
            Entity source,
            in FogProjectionPolicy policy,
            int currentTick)
        {
            if (viewer == Entity.Null || target == Entity.Null || source == Entity.Null)
            {
                return;
            }

            _knowledge.Upsert(viewer, target, CreateKnown(source, in policy, currentTick));
        }

        private static KnowledgeDisclosureRecord CreateLive(Entity source, in FogProjectionPolicy policy, int currentTick)
        {
            return new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                policy.Disclosure.AttributeMask,
                policy.Disclosure.RelationshipTypeMask,
                policy.Disclosure.TagMask,
                source,
                currentTick,
                expiryTick: 0,
                confidencePermille: 1000,
                revision: 0);
        }

        private static KnowledgeDisclosureRecord CreateKnown(Entity source, in FogProjectionPolicy policy, int currentTick)
        {
            int expiry = policy.MemoryTtlTicks > 0 ? currentTick + policy.MemoryTtlTicks : 0;
            return new KnowledgeDisclosureRecord(
                KnowledgePresence.Known,
                KnowledgePositionAccess.LastKnown,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                source,
                currentTick,
                expiry,
                confidencePermille: 650,
                revision: 0);
        }

        private static KnowledgeDisclosureRecord CreateHidden(Entity source, int currentTick)
        {
            return new KnowledgeDisclosureRecord(
                KnowledgePresence.HiddenWithSource,
                KnowledgePositionAccess.None,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                source,
                currentTick,
                expiryTick: 0,
                confidencePermille: 1000,
                revision: 0);
        }

        private static uint LayerMaskFromField(FogField field)
        {
            int value = field.LayerId.Value;
            return value <= 0 || value > 32 ? 0u : 1u << (value - 1);
        }
    }
}
