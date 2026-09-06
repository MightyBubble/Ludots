using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay;
using Ludots.Core.Presentation.Events;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Derived interaction context kernel (#1398 S2b, constitution §8.2/§8.3). The
    /// <c>ActivateContext</c> / <c>DeactivateContext</c> graph ops land here:
    /// <list type="bullet">
    /// <item>Activation is idempotent-failure — activating a context the subject already
    /// carries (base mount or instance) fails fast by name, and a declared parent must be active.</item>
    /// <item>Deactivating a parent removes its descendants transitively (父停用自动清子);
    /// deactivating an inactive context fails fast by name.</item>
    /// <item>Every activation/deactivation publishes
    /// <see cref="PresentationEventKind.ContextActivated"/> /
    /// <see cref="PresentationEventKind.ContextDeactivated"/> (KeyId = context profile id,
    /// PayloadB = parent profile id) so observers — presenter rules, input projection,
    /// anything else — subscribe to interaction context lifecycle as plain event subscribers.
    /// This layer is interaction-domain only: it writes the entity-mounted context state and
    /// publishes lifecycle events, and never touches the presentation domain (no presenter
    /// commands, no scope tags, no presenter bookkeeping). Presentation appearance driven by
    /// context is authored as presenter-side bindings/rules that read that state.</item>
    /// </list>
    /// </summary>
    public sealed class InteractionContextInstanceRuntime
    {
        private readonly World _world;
        private readonly InteractionContextProfileRegistry _profiles;
        private readonly PresentationEventStream _events;
        private readonly GameSession? _session;
        private Action<Entity, int>? _runDeactivatedSlotNow;
        private readonly List<int> _removalScratch = new(capacity: InteractionContextInstances.Capacity);

        public InteractionContextInstanceRuntime(
            World world,
            InteractionContextProfileRegistry profiles,
            PresentationEventStream events,
            GameSession? session = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _session = session;
        }

        /// <summary>
        /// Late-bound change-point hook (#1398 刀3): the mount gate's
        /// <c>RunDeactivatedSlotNow</c>, wired by the engine after both are built. When bound,
        /// <see cref="Deactivate"/> runs each removed context's <c>onDeactivated</c> slot
        /// synchronously on the same change point instead of leaving it to the gate's next
        /// world scan — settlement completes in the deactivating tick.
        /// </summary>
        public void BindDeactivatedSlotRunner(Action<Entity, int>? runDeactivatedSlotNow)
        {
            _runDeactivatedSlotNow = runDeactivatedSlotNow;
        }

        /// <summary>True when the subject carries the context as its base mount or an active instance.</summary>
        public bool IsActive(Entity subject, int profileId)
        {
            if (!_world.IsAlive(subject))
            {
                return false;
            }

            if (_world.TryGet<InteractionContextInstance>(subject, out InteractionContextInstance baseContext) &&
                baseContext.ContextId == profileId)
            {
                return true;
            }

            return _world.TryGet<InteractionContextInstances>(subject, out InteractionContextInstances instances) &&
                instances.IndexOf(profileId) >= 0;
        }

        /// <summary>
        /// Activate a context instance on the subject. Fail-fast: dead subject, unknown context
        /// key, context already active (base mount or instance), parent declared but not
        /// active, or instance capacity exceeded.
        /// </summary>
        public void Activate(Entity subject, int contextKeyId, int parentContextKeyId)
        {
            RequireAliveSubject(subject, nameof(Activate));
            int profileId = RequireProfileId(contextKeyId, nameof(Activate));
            if (IsActive(subject, profileId))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.ActivateContextAlreadyActive: entity {subject} already carries interaction context " +
                    $"'{ProfileName(profileId)}'; instance activation is idempotent-failure by name, not a silent replace.");
            }

            int parentProfileId = 0;
            if (parentContextKeyId > 0)
            {
                parentProfileId = RequireProfileId(parentContextKeyId, nameof(Activate));
                if (!IsActive(subject, parentProfileId))
                {
                    throw new InvalidOperationException(
                        $"GAS.GRAPH.ERR.ActivateContextParentInactive: entity {subject} activates '{ProfileName(profileId)}' " +
                        $"under parent '{ProfileName(parentProfileId)}' but the parent is not active on this entity.");
                }
            }

            if (!_profiles.TryCreateActiveContext(
                    profileId,
                    subject,
                    InteractionContextInstanceSource.ContextInstanceOp,
                    out InteractionContextInstance instance))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.ActivateContextUnknown: interaction context profile id {profileId} is not installed.");
            }

            instance.ParentContextId = parentProfileId;

            if (_world.TryGet<InteractionContextInstances>(subject, out InteractionContextInstances instances))
            {
                instances.Add(instance);
                _world.Set(subject, instances);
            }
            else
            {
                var fresh = new InteractionContextInstances();
                fresh.Add(instance);
                _world.Add(subject, fresh);
            }

            Publish(PresentationEventKind.ContextActivated, subject, instance);
        }

        /// <summary>
        /// Deactivate a context instance on the subject (descendants follow transitively).
        /// Fail-fast: dead subject, unknown context key, or context not mounted as an
        /// instance (base mounts belong to their own lifecycles and are not op-poppable).
        /// </summary>
        public void Deactivate(Entity subject, int contextKeyId)
        {
            RequireAliveSubject(subject, nameof(Deactivate));
            int profileId = RequireProfileId(contextKeyId, nameof(Deactivate));
            if (!_world.TryGet<InteractionContextInstances>(subject, out InteractionContextInstances instances) ||
                instances.IndexOf(profileId) < 0)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.DeactivateContextNotActive: entity {subject} carries no interaction context instance " +
                    $"'{ProfileName(profileId)}'; base mounts belong to their own lifecycles.");
            }

            CollectWithDescendants(instances, profileId, _removalScratch);
            // Snapshot before removal: the Deactivated slots below run user graph bodies that
            // may re-enter this runtime (nested Deactivate reuses the shared scratch).
            int[] removed = _removalScratch.ToArray();
            for (int i = 0; i < removed.Length; i++)
            {
                int index = instances.IndexOf(removed[i]);
                InteractionContextInstance instance = instances[index];
                instances.RemoveAt(index);
                Publish(PresentationEventKind.ContextDeactivated, subject, instance);
            }

            _world.Set(subject, instances);
            _removalScratch.Clear();

            // #1398 刀3: run the onDeactivated slot at the same change point that removed the
            // context, so settlement/preview teardown finish in this tick — no 1-tick delay
            // while waiting for the gate's next world scan. The gate unmounts the profile's
            // triggers and skips re-running the slot on its reconcile pass.
            if (_runDeactivatedSlotNow != null)
            {
                for (int i = 0; i < removed.Length; i++)
                {
                    _runDeactivatedSlotNow(subject, removed[i]);
                }
            }
        }

        private static void CollectWithDescendants(
            in InteractionContextInstances instances,
            int profileId,
            List<int> removal)
        {
            removal.Clear();
            removal.Add(profileId);
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < instances.Count; i++)
                {
                    int candidate = instances[i].ContextId;
                    if (removal.Contains(instances[i].ParentContextId) && !removal.Contains(candidate))
                    {
                        removal.Add(candidate);
                        grew = true;
                    }
                }
            }
        }

        private void Publish(PresentationEventKind kind, Entity subject, in InteractionContextInstance instance)
        {
            var evt = new PresentationEvent
            {
                LogicTickStamp = _session?.CurrentTick ?? 0,
                Kind = kind,
                KeyId = instance.ContextId,
                Source = subject,
                Target = subject,
                Viewer = subject,
                PayloadB = instance.ParentContextId,
            };

            if (!_events.TryAdd(in evt))
            {
                throw new InvalidOperationException(
                    $"PresentationEventStream is full while publishing {kind} for context id {instance.ContextId}.");
            }
        }

        private void RequireAliveSubject(Entity subject, string operation)
        {
            if (_world == null || !_world.IsAlive(subject))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.{operation}SubjectDead: target entity {subject} is not alive.");
            }
        }

        private int RequireProfileId(int contextKeyId, string operation)
        {
            string name = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(contextKeyId);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.{operation}Unknown: context key id {contextKeyId} resolves to no interaction context profile.");
            }

            int profileId = _profiles.ProfileIdRegistry.GetId(name);
            if (!_profiles.IsInstalled(profileId))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.{operation}Unknown: interaction context profile '{name}' is not installed.");
            }

            return profileId;
        }

        private string ProfileName(int profileId)
        {
            return _profiles.ProfileIdRegistry.GetName(profileId) ?? profileId.ToString();
        }
    }
}
