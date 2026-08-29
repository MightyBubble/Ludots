using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;

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
    /// <para>
    /// The same update also reconciles the entity-mounted read face: per control domain, the
    /// topmost frame whose carrier resolves to the domain representative is projected onto that
    /// representative as an <see cref="ActiveInteractionContext"/> component. Entity-side readers
    /// (DEC-14 arbitration) therefore never read the stack, while the stack keeps the frame
    /// lifecycle for the projection, collection routing, and IMC projection consumers.
    /// </para>
    /// </summary>
    public sealed class AbilityExecInteractionContextSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _execQuery = new QueryDescription().WithAll<AbilityExecInstance>();
        private static readonly QueryDescription _activeContextQuery = new QueryDescription().WithAll<ActiveInteractionContext>();

        private readonly InteractionContextStack _contextStack;
        private readonly InteractionContextProfileRegistry _contextProfiles;
        private readonly AbilityDefinitionRegistry _abilityDefinitions;
        private readonly ControlDomainQuery _controlDomains;

        private Entity[] _trackedEntities = new Entity[16];
        private int[] _trackedAbilityIds = new int[16];
        private int _trackedCount;
        private Entity[] _scratch = new Entity[64];
        private Entity[] _mountedScratch = new Entity[8];
        private Entity[] _desiredReps = new Entity[8];
        private ActiveInteractionContext[] _desiredStates = new ActiveInteractionContext[8];
        private bool[] _desiredMounted = new bool[8];
        private int _desiredCount;

        public AbilityExecInteractionContextSystem(
            World world,
            InteractionContextStack contextStack,
            InteractionContextProfileRegistry contextProfiles,
            AbilityDefinitionRegistry abilityDefinitions,
            ControlDomainQuery controlDomains)
            : base(world)
        {
            _contextStack = contextStack ?? throw new ArgumentNullException(nameof(contextStack));
            _contextProfiles = contextProfiles ?? throw new ArgumentNullException(nameof(contextProfiles));
            _abilityDefinitions = abilityDefinitions ?? throw new ArgumentNullException(nameof(abilityDefinitions));
            _controlDomains = controlDomains ?? throw new ArgumentNullException(nameof(controlDomains));
        }

        public override void Update(in float dt)
        {
            ReclaimEndedExecFrames();
            PushStartedExecFrames();
            ReconcileActiveContextState();
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

        /// <summary>
        /// Project the frame lifecycle onto entity-mounted interaction state: per control domain,
        /// the topmost frame whose carrier resolves to the domain representative wins (LIFO, the
        /// stack's top-frame arbitration). A mounted component whose carrier still owns a stack
        /// frame but no longer resolves (dead or domain-less) stays frozen for the reclaim window
        /// so entity-side readers fail closed exactly like the retired top-frame read; once the
        /// carrier leaves the stack, the component is released back to the steady state.
        /// </summary>
        private void ReconcileActiveContextState()
        {
            CollectDesiredContexts();
            RefreshMountedContexts();
            MountMissingContexts();
        }

        private void CollectDesiredContexts()
        {
            _desiredCount = 0;
            for (int i = _contextStack.Count - 1; i >= 0; i--)
            {
                if (!_contextStack.TryGetAt(i, out InteractionContextFrame frame))
                {
                    continue;
                }

                if (frame.ContextEntity == default)
                {
                    continue;
                }

                if (!_controlDomains.TryResolveControlDomain(frame.ContextEntity, out Entity domainRep) ||
                    domainRep == default)
                {
                    continue;
                }

                if (ContainsDesiredRep(domainRep))
                {
                    continue;
                }

                if (_desiredCount == _desiredReps.Length)
                {
                    Array.Resize(ref _desiredReps, _desiredCount * 2);
                    Array.Resize(ref _desiredStates, _desiredCount * 2);
                    Array.Resize(ref _desiredMounted, _desiredCount * 2);
                }

                _desiredReps[_desiredCount] = domainRep;
                _desiredStates[_desiredCount] = new ActiveInteractionContext
                {
                    ContextId = frame.ContextId,
                    ContextEntity = frame.ContextEntity,
                    CommandIntentProfileId = frame.CommandIntentProfileId,
                };
                _desiredMounted[_desiredCount] = false;
                _desiredCount++;
            }
        }

        private void RefreshMountedContexts()
        {
            int mountedCount = World.CountEntities(in _activeContextQuery);
            if (mountedCount == 0)
            {
                return;
            }

            if (mountedCount > _mountedScratch.Length)
            {
                _mountedScratch = new Entity[mountedCount * 2];
            }

            World.GetEntities(in _activeContextQuery, _mountedScratch);
            for (int i = 0; i < mountedCount; i++)
            {
                Entity holder = _mountedScratch[i];
                if (!World.IsAlive(holder))
                {
                    continue;
                }

                if (TryFindDesired(holder, out int desiredIndex))
                {
                    ref ActiveInteractionContext mounted = ref World.Get<ActiveInteractionContext>(holder);
                    if (mounted.ContextId != _desiredStates[desiredIndex].ContextId ||
                        mounted.ContextEntity != _desiredStates[desiredIndex].ContextEntity ||
                        mounted.CommandIntentProfileId != _desiredStates[desiredIndex].CommandIntentProfileId)
                    {
                        mounted = _desiredStates[desiredIndex];
                    }

                    _desiredMounted[desiredIndex] = true;
                    continue;
                }

                if (CarrierStillOnStack(World.Get<ActiveInteractionContext>(holder).ContextEntity))
                {
                    continue;
                }

                World.Remove<ActiveInteractionContext>(holder);
            }
        }

        private void MountMissingContexts()
        {
            for (int i = 0; i < _desiredCount; i++)
            {
                if (_desiredMounted[i] || !World.IsAlive(_desiredReps[i]))
                {
                    continue;
                }

                World.Add(_desiredReps[i], _desiredStates[i]);
            }
        }

        private bool ContainsDesiredRep(Entity rep)
        {
            for (int i = 0; i < _desiredCount; i++)
            {
                if (_desiredReps[i] == rep)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFindDesired(Entity rep, out int index)
        {
            for (int i = 0; i < _desiredCount; i++)
            {
                if (_desiredReps[i] == rep)
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private bool CarrierStillOnStack(Entity carrier)
        {
            for (int i = 0; i < _contextStack.Count; i++)
            {
                if (_contextStack.TryGetAt(i, out InteractionContextFrame frame) &&
                    frame.ContextEntity == carrier)
                {
                    return true;
                }
            }

            return false;
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
