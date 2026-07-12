using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;

namespace ControlPlaneProjectionShowcaseMod.Runtime
{
    /// <summary>
    /// Resolved anchors, registry ids, and the proxy toggle for the control plane projection showcase.
    /// Shared by the mod systems and the WebUI dataplane so input action and browser command run the
    /// exact same code path.
    /// </summary>
    public sealed class ControlPlaneProjectionScenarioState
    {
        private World? _world;
        private TagOps? _tagOps;

        public bool Ready { get; private set; }

        public Entity P1Rep { get; set; }
        public Entity P2Rep { get; set; }
        public Entity TeamRep { get; set; }
        public Entity RefereeRep { get; set; }
        public Entity ForeignRep { get; set; }
        public Entity RefereeOwnedMarker { get; set; }
        public Entity ForeignMarker { get; set; }
        public Entity[] P1Units { get; } = new Entity[ControlPlaneProjectionShowcaseIds.P1UnitInstanceIds.Length];
        public Entity[] P2Units { get; } = new Entity[ControlPlaneProjectionShowcaseIds.P2UnitInstanceIds.Length];

        public int OwnsTypeId { get; set; } = -1;
        public int ControlsTypeId { get; set; } = -1;
        public int MemberOfTypeId { get; set; } = -1;
        public int AllyTypeId { get; set; } = -1;
        public int OfflineTagId { get; set; } = -1;

        public int CommandSourceKeyId { get; set; }
        public int OwnedProjectionKeyId { get; set; }
        public int ProxiedProjectionKeyId { get; set; }
        public int RefereePhase0ProjectionKeyId { get; set; }
        public int RefereePhase1ProjectionKeyId { get; set; }
        public bool RefereeReady { get; set; }
        public bool RefereeP2GrantActive { get; set; }

        /// <summary>
        /// Whether the trigger tag is currently present on P2Rep. The Controls(P1Rep→P2Rep) grant itself is
        /// owned by the AssociationControlProfileRuntime (RFC-0065 CTRL-4b), which reacts to this tag.
        /// </summary>
        public bool ProxyActive
        {
            get
            {
                if (!Ready || _world == null || !_world.IsAlive(P2Rep))
                {
                    return false;
                }

                ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(P2Rep);
                return tags.HasTag(OfflineTagId);
            }
        }

        public void BindRuntime(World world, TagOps tagOps)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            Ready = true;
        }

        /// <summary>
        /// Flips the trigger tag on P2Rep — nothing else. The Controls edge is granted/revoked by the
        /// AssociationControlProfileRuntime on its next evaluation pass, proving the M3 chain is
        /// trigger-agnostic: any writer of the tag drives the same control transfer.
        /// </summary>
        public bool ToggleProxy()
        {
            if (!Ready || _world == null || _tagOps == null)
            {
                throw new InvalidOperationException("Control plane projection scenario is not bootstrapped yet.");
            }

            ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(P2Rep);
            bool activate = !tags.HasTag(OfflineTagId);
            if (activate)
            {
                _tagOps.AddTag(_world, P2Rep, OfflineTagId);
            }
            else
            {
                _tagOps.RemoveTag(_world, P2Rep, OfflineTagId);
            }

            return activate;
        }
    }
}
