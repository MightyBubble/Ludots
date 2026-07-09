using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// RFC-0065 CTX-6: binds ability exec lifecycles to interaction context frames (DEC-13 post-order
    /// targeting sessions). The exec instance component (<see cref="AbilityExecInstance"/>) is the
    /// single sim-side lifecycle carrier: while an exec of an ability declaring
    /// <c>interactionContextProfile</c> runs, the profile's frame sits on the stack with the exec
    /// carrier entity as <c>ContextEntity</c>; when the exec ends for any reason — finish, interrupt,
    /// fail, order cancel, or caster death — the frame is reclaimed via
    /// <see cref="InteractionContextStack.RemoveByContextEntity"/>. Reconciliation is polling over
    /// component existence (no new event kinds, deterministic across every exec teardown path).
    /// Steady state is allocation free.
    /// </summary>
    public sealed class AbilityExecInteractionContextSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _execQuery = new QueryDescription().WithAll<AbilityExecInstance>();

        private readonly InteractionContextStack _contextStack;
        private readonly InteractionContextProfileRegistry _contextProfiles;
        private readonly AbilityDefinitionRegistry _abilityDefinitions;

        private Entity[] _trackedEntities = new Entity[16];
        private int[] _trackedAbilityIds = new int[16];
        private int _trackedCount;
        private Entity[] _scratch = new Entity[64];

        public AbilityExecInteractionContextSystem(
            World world,
            InteractionContextStack contextStack,
            InteractionContextProfileRegistry contextProfiles,
            AbilityDefinitionRegistry abilityDefinitions)
            : base(world)
        {
            _contextStack = contextStack ?? throw new ArgumentNullException(nameof(contextStack));
            _contextProfiles = contextProfiles ?? throw new ArgumentNullException(nameof(contextProfiles));
            _abilityDefinitions = abilityDefinitions ?? throw new ArgumentNullException(nameof(abilityDefinitions));
        }

        public override void Update(in float dt)
        {
            ReclaimEndedExecFrames();
            PushStartedExecFrames();
        }

        /// <summary>
        /// Remove frames whose exec ended: carrier dead, exec component removed, or the slot now
        /// executes a different ability. Token-free removal by context entity covers abort/death.
        /// </summary>
        private void ReclaimEndedExecFrames()
        {
            for (int i = _trackedCount - 1; i >= 0; i--)
            {
                Entity carrier = _trackedEntities[i];
                if (World.IsAlive(carrier) && World.Has<AbilityExecInstance>(carrier))
                {
                    ref AbilityExecInstance exec = ref World.Get<AbilityExecInstance>(carrier);
                    if (exec.AbilityId == _trackedAbilityIds[i])
                    {
                        continue;
                    }
                }

                _contextStack.RemoveByContextEntity(carrier);
                int last = _trackedCount - 1;
                _trackedEntities[i] = _trackedEntities[last];
                _trackedAbilityIds[i] = _trackedAbilityIds[last];
                _trackedCount = last;
            }
        }

        private void PushStartedExecFrames()
        {
            int execCount = World.CountEntities(in _execQuery);
            if (execCount == 0)
            {
                return;
            }

            if (execCount > _scratch.Length)
            {
                _scratch = new Entity[execCount * 2];
            }

            World.GetEntities(in _execQuery, _scratch);
            for (int i = 0; i < execCount; i++)
            {
                Entity carrier = _scratch[i];
                if (IsTracked(carrier))
                {
                    continue;
                }

                ref AbilityExecInstance exec = ref World.Get<AbilityExecInstance>(carrier);
                int abilityId = exec.AbilityId;
                if (!_abilityDefinitions.TryGetInteractionContextProfileId(abilityId, out string profileName))
                {
                    continue;
                }

                int profileId = _contextProfiles.ProfileIdRegistry.GetId(profileName);
                if (!_contextProfiles.TryCreateFrameDescriptor(profileId, carrier, out InteractionContextFrameDescriptor descriptor))
                {
                    throw new InvalidOperationException(
                        $"Ability id {abilityId} declares interaction context profile '{profileName}' which is not installed.");
                }

                _contextStack.Push(in descriptor);
                Track(carrier, abilityId);
            }
        }

        private bool IsTracked(Entity carrier)
        {
            for (int i = 0; i < _trackedCount; i++)
            {
                if (_trackedEntities[i] == carrier)
                {
                    return true;
                }
            }

            return false;
        }

        private void Track(Entity carrier, int abilityId)
        {
            if (_trackedCount == _trackedEntities.Length)
            {
                Array.Resize(ref _trackedEntities, _trackedCount * 2);
                Array.Resize(ref _trackedAbilityIds, _trackedCount * 2);
            }

            _trackedEntities[_trackedCount] = carrier;
            _trackedAbilityIds[_trackedCount] = abilityId;
            _trackedCount++;
        }
    }
}
