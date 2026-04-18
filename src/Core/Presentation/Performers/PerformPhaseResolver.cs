using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Sole translator from raw visibility, ownership, and relationship hints into perform phase contracts.
    /// </summary>
    public sealed class PerformPhaseResolver
    {
        public PerformAudienceContext CreateAudienceContext(
            Entity viewer,
            Team? viewerTeam = null,
            PlayerOwner? viewerOwner = null,
            bool revealHidden = false)
        {
            return new PerformAudienceContext
            {
                Viewer = viewer,
                ViewerTeam = viewerTeam ?? default,
                HasViewerTeam = viewerTeam.HasValue,
                ViewerOwner = viewerOwner ?? default,
                HasViewerOwner = viewerOwner.HasValue,
                RevealHidden = revealHidden,
            };
        }

        public PerformAudienceContext CreateAudienceContext(World world, Entity viewer, bool revealHidden = false)
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

        public PerformPhaseInput CreateInput(
            in PerformAudienceContext audience,
            Entity owner,
            in CullState cullState,
            bool hasVision,
            Team? ownerTeam = null,
            PlayerOwner? ownerOwner = null,
            RelationshipRuntime? relationships = null)
        {
            TeamRelationship? teamRelationship = null;
            if (audience.HasViewerTeam && ownerTeam.HasValue)
            {
                teamRelationship = TeamManager.GetRelationship(audience.ViewerTeam.Id, ownerTeam.Value.Id);
            }

            bool isOwnedByAudience = audience.HasViewerOwner
                && ownerOwner.HasValue
                && audience.ViewerOwner.PlayerId == ownerOwner.Value.PlayerId;

            bool hasRelationshipLink = relationships != null
                && audience.HasViewer
                && owner != Entity.Null
                && (relationships.HasLink(audience.Viewer, owner) || relationships.HasLink(owner, audience.Viewer));

            return new PerformPhaseInput
            {
                Audience = audience,
                Owner = owner,
                OwnerTeam = ownerTeam ?? default,
                OwnerOwner = ownerOwner ?? default,
                HasOwnerTeam = ownerTeam.HasValue,
                HasOwnerOwner = ownerOwner.HasValue,
                IsVisible = cullState.IsVisible,
                IsCulled = cullState.LOD == LODLevel.Culled,
                HasVision = hasVision,
                HasRelationshipLink = hasRelationshipLink,
                HasTeamRelationship = teamRelationship.HasValue,
                IsOwnedByAudience = isOwnedByAudience,
                TeamRelationship = teamRelationship ?? default,
                LOD = cullState.LOD,
            };
        }

        public PerformPhaseInput CreateInput(
            World world,
            Entity owner,
            in PerformAudienceContext audience,
            RelationshipRuntime? relationships = null,
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

            return CreateInput(audience, owner, in cullState, hasVision, ownerTeam, ownerOwner, relationships);
        }

        public PerformPhaseResult Resolve(in PerformPhaseInput input)
        {
            bool revealHidden = input.Audience.RevealHidden;
            bool hasVision = revealHidden || input.HasVision;
            bool isCulled = !revealHidden && (input.IsCulled || input.LOD == LODLevel.Culled);
            bool isVisible = revealHidden || (input.IsVisible && hasVision && !isCulled);
            LODLevel lod = revealHidden && input.LOD == LODLevel.Culled
                ? LODLevel.High
                : input.LOD;

            bool isFriendly = input.IsOwnedByAudience
                || (input.HasTeamRelationship && input.TeamRelationship == TeamRelationship.Friendly);
            bool isHostile = input.HasTeamRelationship && input.TeamRelationship == TeamRelationship.Hostile;
            bool shouldPresent = isVisible && !isCulled;
            bool allowWorldHudProjection = shouldPresent &&
                                           (input.IsOwnedByAudience || isFriendly || input.Audience.RevealHidden);

            return new PerformPhaseResult
            {
                ShouldPresent = shouldPresent,
                AllowWorldHudProjection = allowWorldHudProjection,
                IsVisible = isVisible,
                IsCulled = isCulled,
                HasVision = hasVision,
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
