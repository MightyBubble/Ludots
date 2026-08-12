using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Sole translator from visibility/cull and knowledge projection facts into present phase contracts.
    /// Team/ownership facts remain available for styling; they do not gate world HUD projection.
    /// </summary>
    public sealed class PresentPhaseResolver
    {
        public PresentAudienceContext CreateAudienceContext(
            Entity viewer,
            Team? viewerTeam = null,
            PlayerOwner? viewerOwner = null,
            bool revealHidden = false)
        {
            return new PresentAudienceContext
            {
                Viewer = viewer,
                ViewerTeam = viewerTeam ?? default,
                HasViewerTeam = viewerTeam.HasValue,
                ViewerOwner = viewerOwner ?? default,
                HasViewerOwner = viewerOwner.HasValue,
                RevealHidden = revealHidden,
            };
        }

        public PresentAudienceContext CreateAudienceContext(World world, Entity viewer, bool revealHidden = false)
        {
            Team? viewerTeam = null;
            PlayerOwner? viewerOwner = null;

            if (world.IsAlive(viewer) && world.TryGet(viewer, out Team resolvedViewerTeam))
            {
                viewerTeam = resolvedViewerTeam;
            }

            if (world.IsAlive(viewer) && world.TryGet(viewer, out PlayerOwner resolvedViewerOwner))
            {
                viewerOwner = resolvedViewerOwner;
            }

            return CreateAudienceContext(viewer, viewerTeam, viewerOwner, revealHidden);
        }

        public PresentPhaseInput CreateInput(
            in PresentAudienceContext audience,
            Entity owner,
            in CullState cullState,
            bool hasVision,
            Team? ownerTeam = null,
            PlayerOwner? ownerOwner = null,
            bool hasRelationshipLink = false)
        {
            return CreateInput(
                audience,
                owner,
                in cullState,
                hasVision,
                default,
                ReadOnlySpan<int>.Empty,
                ownerTeam,
                ownerOwner,
                hasRelationshipLink);
        }

        public PresentPhaseInput CreateInput(
            in PresentAudienceContext audience,
            Entity owner,
            in CullState cullState,
            in PresentProjectionFacts projection,
            ReadOnlySpan<int> requiredAttributeIds,
            Team? ownerTeam = null,
            PlayerOwner? ownerOwner = null,
            bool hasRelationshipLink = false)
        {
            return CreateInput(
                audience,
                owner,
                in cullState,
                projection.CanReadPosition(Ludots.Core.Knowledge.KnowledgePositionAccess.Live),
                projection,
                requiredAttributeIds,
                ownerTeam,
                ownerOwner,
                hasRelationshipLink);
        }

        private PresentPhaseInput CreateInput(
            in PresentAudienceContext audience,
            Entity owner,
            in CullState cullState,
            bool hasVision,
            in PresentProjectionFacts projection,
            ReadOnlySpan<int> requiredAttributeIds,
            Team? ownerTeam = null,
            PlayerOwner? ownerOwner = null,
            bool hasRelationshipLink = false)
        {
            TeamRelationship? teamRelationship = null;
            if (audience.HasViewerTeam && ownerTeam.HasValue)
            {
                teamRelationship = TeamManager.GetRelationship(audience.ViewerTeam.Id, ownerTeam.Value.Id);
            }

            bool isOwnedByAudience = audience.HasViewerOwner
                && ownerOwner.HasValue
                && audience.ViewerOwner.PlayerId == ownerOwner.Value.PlayerId;
            bool requiresAttributeProjection = !requiredAttributeIds.IsEmpty;
            bool hasAttributeProjection = !requiresAttributeProjection || projection.CanReadAttributes(requiredAttributeIds);

            return new PresentPhaseInput
            {
                Audience = audience,
                Owner = owner,
                OwnerTeam = ownerTeam ?? default,
                OwnerOwner = ownerOwner ?? default,
                HasOwnerTeam = ownerTeam.HasValue,
                HasOwnerOwner = ownerOwner.HasValue,
                IsVisible = cullState.IsVisible,
                IsCulled = !cullState.IsVisible,
                HasVision = hasVision,
                RequiresAttributeProjection = requiresAttributeProjection,
                HasAttributeProjection = hasAttributeProjection,
                HasRelationshipLink = hasRelationshipLink,
                HasTeamRelationship = teamRelationship.HasValue,
                IsOwnedByAudience = isOwnedByAudience,
                TeamRelationship = teamRelationship ?? default,
                Projection = projection,
                LOD = cullState.LOD,
            };
        }

        public PresentPhaseInput CreateInput(
            World world,
            Entity owner,
            in PresentAudienceContext audience,
            bool hasRelationshipLink = false,
            bool hasVision = true)
        {
            var cullState = new CullState
            {
                IsVisible = true,
                LOD = LODLevel.High,
            };

            Team? ownerTeam = null;
            PlayerOwner? ownerOwner = null;

            if (world.IsAlive(owner) && world.TryGet(owner, out CullState resolvedCullState))
            {
                cullState = resolvedCullState;
            }

            if (world.IsAlive(owner) && world.TryGet(owner, out Team resolvedOwnerTeam))
            {
                ownerTeam = resolvedOwnerTeam;
            }

            if (world.IsAlive(owner) && world.TryGet(owner, out PlayerOwner resolvedOwnerOwner))
            {
                ownerOwner = resolvedOwnerOwner;
            }

            return CreateInput(audience, owner, in cullState, hasVision, ownerTeam, ownerOwner, hasRelationshipLink);
        }

        public PresentPhaseInput CreateInput(
            World world,
            Entity owner,
            in PresentAudienceContext audience,
            in PresentProjectionFacts projection,
            ReadOnlySpan<int> requiredAttributeIds,
            bool hasRelationshipLink = false)
        {
            var cullState = new CullState
            {
                IsVisible = true,
                LOD = LODLevel.High,
            };

            Team? ownerTeam = null;
            PlayerOwner? ownerOwner = null;

            if (world.IsAlive(owner) && world.TryGet(owner, out CullState resolvedCullState))
            {
                cullState = resolvedCullState;
            }

            if (world.IsAlive(owner) && world.TryGet(owner, out Team resolvedOwnerTeam))
            {
                ownerTeam = resolvedOwnerTeam;
            }

            if (world.IsAlive(owner) && world.TryGet(owner, out PlayerOwner resolvedOwnerOwner))
            {
                ownerOwner = resolvedOwnerOwner;
            }

            return CreateInput(audience, owner, in cullState, in projection, requiredAttributeIds, ownerTeam, ownerOwner, hasRelationshipLink);
        }

        public PresentPhaseResult Resolve(in PresentPhaseInput input)
        {
            bool revealHidden = input.Audience.RevealHidden;
            bool hasVision = revealHidden || input.HasVision ||
                             (input.AllowVisibleTransientWorldText && input.IsVisible);
            bool isCulled = !revealHidden && input.IsCulled;
            bool isVisible = revealHidden || (input.IsVisible && hasVision && !isCulled);
            LODLevel lod = revealHidden && input.LOD == LODLevel.Culled
                ? LODLevel.High
                : input.LOD;

            bool isFriendly = input.IsOwnedByAudience
                || (input.HasTeamRelationship && input.TeamRelationship == TeamRelationship.Friendly);
            bool isHostile = input.HasTeamRelationship && input.TeamRelationship == TeamRelationship.Hostile;
            bool shouldPresent = isVisible && !isCulled;
            // Knowledge projection is the sole readability authority for attribute HUD.
            // Team/ownership remain styling facts only (see IsFriendly/IsHostile/IsOwnedByAudience).
            bool allowWorldHudProjection = shouldPresent && input.HasAttributeProjection;

            return new PresentPhaseResult
            {
                ShouldPresent = shouldPresent,
                AllowWorldHudProjection = allowWorldHudProjection,
                IsVisible = isVisible,
                IsCulled = isCulled,
                HasVision = hasVision,
                HasKnowledgeProjection = input.Projection.CanKnowEntity,
                RequiresAttributeProjection = input.RequiresAttributeProjection,
                HasAttributeProjection = input.HasAttributeProjection,
                LOD = lod,
                TeamRelationship = input.TeamRelationship,
                IsOwnedByAudience = input.IsOwnedByAudience,
                HasRelationshipLink = input.HasRelationshipLink,
                IsFriendly = isFriendly,
                IsHostile = isHostile,
            };
        }
    }
}
