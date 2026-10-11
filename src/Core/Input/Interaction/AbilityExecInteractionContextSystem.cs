using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// RFC-0065 CTX-6: binds ability exec lifecycles to the entity-mounted active interaction
    /// context (DEC-13 post-order targeting sessions). The exec instance component
    /// (<see cref="AbilityExecInstance"/>) is the single sim-side lifecycle carrier: while an
    /// exec of an ability declaring <c>interactionContextProfile</c> runs, the profile is
    /// mounted as an <see cref="InteractionContextInstance"/> on the carrier's control-domain
    /// representative; when the exec ends for any reason — finish, interrupt, fail, order
    /// cancel, or caster death — the mount is reclaimed in the next update. Reconciliation is
    /// polling over component existence (no new event kinds, deterministic across every exec
    /// teardown path) and per control domain the latest-activated carrier wins (LIFO). A
    /// mount whose carrier dies stays mounted until this system's next update so entity-side
    /// readers fail closed instead of silently falling back to the steady state. Steady state
    /// is allocation free.
    /// <para>
    /// This system manages only <see cref="InteractionContextInstanceSource.ExecLifecycle"/>
    /// mounts; cast commit <c>pushFrame</c> op mounts live and die with their own ops.
    /// </para>
    /// </summary>
    public sealed class AbilityExecInteractionContextSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _execQuery = new QueryDescription().WithAll<AbilityExecInstance>();
        private static readonly QueryDescription _activeContextQuery = new QueryDescription().WithAll<InteractionContextInstance>();

        private readonly InteractionContextProfileRegistry _contextProfiles;
        private readonly AbilityDefinitionRegistry _abilityDefinitions;
        private readonly ControlDomainQuery _controlDomains;

        private Entity[] _trackedEntities = new Entity[16];
        private int[] _trackedAbilityIds = new int[16];
        private int[] _trackedProfileIds = new int[16];
        private int _trackedCount;
        // Per desired rep: the base instance an exec mount displaced (template/spawn/op
        // mounted), restored when the exec lifecycle reclaims. Null entity = none saved.
        private Entity[] _savedBaseReps = new Entity[8];
        private InteractionContextInstance[] _savedBaseStates = new InteractionContextInstance[8];
        private bool[] _savedBaseValid = new bool[8];
        private int _savedBaseCount;
        private Entity[] _scratch = new Entity[64];
        private Entity[] _mountedScratch = new Entity[8];
        private Entity[] _desiredReps = new Entity[8];
        private InteractionContextInstance[] _desiredStates = new InteractionContextInstance[8];
        private bool[] _desiredMounted = new bool[8];
        private int _desiredCount;

        public AbilityExecInteractionContextSystem(
            World world,
            InteractionContextProfileRegistry contextProfiles,
            AbilityDefinitionRegistry abilityDefinitions,
            ControlDomainQuery controlDomains)
            : base(world)
        {
            _contextProfiles = contextProfiles ?? throw new ArgumentNullException(nameof(contextProfiles));
            _abilityDefinitions = abilityDefinitions ?? throw new ArgumentNullException(nameof(abilityDefinitions));
            _controlDomains = controlDomains ?? throw new ArgumentNullException(nameof(controlDomains));
        }

        public override void Update(in float dt)
        {
            ReclaimEndedExecContexts();
            TrackStartedExecContexts();
            ReconcileActiveContextState();
        }

        /// <summary>
        /// Drop tracking for execs that ended: carrier dead, exec component removed, or the
        /// slot now executes a different ability. The carrier's mounted context (if any) is
        /// released by the reconciliation below in the same update.
        /// </summary>
        private void ReclaimEndedExecContexts()
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

                int last = _trackedCount - 1;
                _trackedEntities[i] = _trackedEntities[last];
                _trackedAbilityIds[i] = _trackedAbilityIds[last];
                _trackedProfileIds[i] = _trackedProfileIds[last];
                _trackedCount = last;
            }
        }

        private void TrackStartedExecContexts()
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
                if (!_contextProfiles.IsInstalled(profileId))
                {
                    throw new InvalidOperationException(
                        $"Ability id {abilityId} declares interaction context profile '{profileName}' which is not installed.");
                }

                Track(carrier, abilityId, profileId);
            }
        }

        /// <summary>
        /// Project the exec lifecycles onto entity-mounted interaction state: per control
        /// domain, the latest-activated tracked carrier resolving to the domain representative
        /// wins (LIFO). Mounts this system no longer desires are released back to the steady
        /// state; cast commit op mounts are foreign and stay untouched.
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
            for (int i = _trackedCount - 1; i >= 0; i--)
            {
                Entity carrier = _trackedEntities[i];
                if (!_controlDomains.TryResolveControlDomain(carrier, out Entity domainRep) ||
                    domainRep == default)
                {
                    continue;
                }

                if (ContainsDesiredRep(domainRep))
                {
                    continue;
                }

                if (!_contextProfiles.TryCreateActiveContext(
                        _trackedProfileIds[i],
                        carrier,
                        InteractionContextInstanceSource.ExecLifecycle,
                        out InteractionContextInstance state))
                {
                    throw new InvalidOperationException(
                        $"Ability exec carrier {carrier} declares interaction context profile id {_trackedProfileIds[i]} which is not installed.");
                }

                if (_desiredCount == _desiredReps.Length)
                {
                    Array.Resize(ref _desiredReps, _desiredCount * 2);
                    Array.Resize(ref _desiredStates, _desiredCount * 2);
                    Array.Resize(ref _desiredMounted, _desiredCount * 2);
                }

                _desiredReps[_desiredCount] = domainRep;
                _desiredStates[_desiredCount] = state;
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
                    ref InteractionContextInstance mounted = ref World.Get<InteractionContextInstance>(holder);
                    if (mounted.Source != InteractionContextInstanceSource.ExecLifecycle &&
                        !Equals(mounted, _desiredStates[desiredIndex]))
                    {
                        RememberBase(holder, in mounted);
                        mounted = _desiredStates[desiredIndex];
                    }
                    else if (!Equals(mounted, _desiredStates[desiredIndex]))
                    {
                        mounted = _desiredStates[desiredIndex];
                    }

                    _desiredMounted[desiredIndex] = true;
                    continue;
                }

                // Foreign lifecycles own their mounts: cast commit ops pop their own frames
                // and template-spawn mounts live until the entity dies — this
                // reconciliation reclaims only its own exec-carried contexts.
                if (World.Get<InteractionContextInstance>(holder).Source != InteractionContextInstanceSource.ExecLifecycle)
                {
                    continue;
                }

                World.Remove<InteractionContextInstance>(holder);
                if (TryTakeSavedBase(holder, out InteractionContextInstance savedBase) &&
                    World.IsAlive(savedBase.ContextEntity))
                {
                    World.Add(holder, savedBase);
                }
            }
        }

        private void RememberBase(Entity rep, in InteractionContextInstance instance)
        {
            for (int i = 0; i < _savedBaseCount; i++)
            {
                if (_savedBaseReps[i] == rep)
                {
                    return;
                }
            }

            if (_savedBaseCount == _savedBaseReps.Length)
            {
                Array.Resize(ref _savedBaseReps, _savedBaseCount * 2);
                Array.Resize(ref _savedBaseStates, _savedBaseCount * 2);
                Array.Resize(ref _savedBaseValid, _savedBaseCount * 2);
            }

            _savedBaseReps[_savedBaseCount] = rep;
            _savedBaseStates[_savedBaseCount] = instance;
            _savedBaseValid[_savedBaseCount] = true;
            _savedBaseCount++;
        }

        private bool TryTakeSavedBase(Entity rep, out InteractionContextInstance instance)
        {
            instance = default;
            for (int i = 0; i < _savedBaseCount; i++)
            {
                if (_savedBaseReps[i] == rep)
                {
                    instance = _savedBaseStates[i];
                    int last = _savedBaseCount - 1;
                    _savedBaseReps[i] = _savedBaseReps[last];
                    _savedBaseStates[i] = _savedBaseStates[last];
                    _savedBaseValid[i] = _savedBaseValid[last];
                    _savedBaseCount = last;
                    return true;
                }
            }

            return false;
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

        private static bool Equals(in InteractionContextInstance left, in InteractionContextInstance right)
        {
            return left.ContextId == right.ContextId &&
                left.ContextEntity == right.ContextEntity &&
                left.CommandIntentProfileId == right.CommandIntentProfileId &&
                left.FilterProfileId == right.FilterProfileId &&
                left.InputContextId == right.InputContextId &&
                left.Source == right.Source;
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

        private void Track(Entity carrier, int abilityId, int profileId)
        {
            if (_trackedCount == _trackedEntities.Length)
            {
                Array.Resize(ref _trackedEntities, _trackedCount * 2);
                Array.Resize(ref _trackedAbilityIds, _trackedCount * 2);
                Array.Resize(ref _trackedProfileIds, _trackedCount * 2);
            }

            _trackedEntities[_trackedCount] = carrier;
            _trackedAbilityIds[_trackedCount] = abilityId;
            _trackedProfileIds[_trackedCount] = profileId;
            _trackedCount++;
        }
    }
}
