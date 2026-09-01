using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Derived interaction context kernel (#1398 S2b, constitution §8.2/§8.3). The
    /// <c>ActivateContext</c> / <c>DeactivateContext</c> graph ops land here:
    /// <list type="bullet">
    /// <item>Activation is idempotent-failure — activating a context the subject already
    /// carries (base mount or instance) fails fast by name, and a declared parent must be active.</item>
    /// <item>Each instance owns a presenter scope tag (band arithmetic per subject × context,
    /// disjoint from the ability-aim scope band); presenters created while the context is
    /// active bind that scope, and deactivation clears the whole scope through the formal
    /// <see cref="PresenterCommand"/>(<see cref="PresenterCommandKind.DestroyPresenterScope"/>)
    /// binding point — no ad-hoc presenter runtime calls from graph land.</item>
    /// <item>Deactivating a parent removes its descendants transitively (父停用自动清子);
    /// deactivating an inactive context fails fast by name.</item>
    /// <item>Every activation/deactivation publishes
    /// <see cref="PresentationEventKind.ContextActivated"/> /
    /// <see cref="PresentationEventKind.ContextDeactivated"/> (key = context profile id) so
    /// presenter rules observe context lifecycle as plain event subscribers.</item>
    /// </list>
    /// </summary>
    public sealed class InteractionContextInstanceRuntime
    {
        /// <summary>
        /// Scope band base for interaction context instances inside the per-owner scope band
        /// arithmetic (<c>owner.Id * 100000 + offset</c>): the ability-aim family occupies
        /// 44000-44002, context instances start at 45000, leaving headroom for profile ids
        /// below 55000. Scope ids stay opaque ints on the presenter side.
        /// </summary>
        public const int ScopeBandBase = 45000;

        private const int OwnerBand = 100000;

        private readonly World _world;
        private readonly InteractionContextProfileRegistry _profiles;
        private readonly PresentationEventStream _events;
        private readonly PresenterCommandBuffer _commands;
        private readonly GameSession? _session;
        private readonly List<int> _removalScratch = new(capacity: InteractionContextInstances.Capacity);

        public InteractionContextInstanceRuntime(
            World world,
            InteractionContextProfileRegistry profiles,
            PresentationEventStream events,
            PresenterCommandBuffer commands,
            GameSession? session = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _session = session;
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

        /// <summary>Scope tag of an active instance; false when not active.</summary>
        public bool TryGetScopeTag(Entity subject, int profileId, out int scopeTag)
        {
            scopeTag = 0;
            if (!_world.IsAlive(subject) ||
                !_world.TryGet<InteractionContextInstances>(subject, out InteractionContextInstances instances))
            {
                return false;
            }

            int index = instances.IndexOf(profileId);
            if (index < 0)
            {
                return false;
            }

            scopeTag = instances[index].ScopeTag;
            return true;
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
            instance.ScopeTag = ContextScopeTag(subject, profileId);

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
            for (int i = 0; i < _removalScratch.Count; i++)
            {
                int index = instances.IndexOf(_removalScratch[i]);
                InteractionContextInstance instance = instances[index];
                instances.RemoveAt(index);
                EnqueueScopeDestroy(instance);
                Publish(PresentationEventKind.ContextDeactivated, subject, instance);
            }

            _world.Set(subject, instances);
            _removalScratch.Clear();
        }

        /// <summary>Deterministic per-subject scope tag (band arithmetic, see <see cref="ScopeBandBase"/>).</summary>
        public static int ContextScopeTag(Entity subject, int profileId)
        {
            unchecked
            {
                int scope = (subject.Id * OwnerBand) + ScopeBandBase + profileId;
                return scope <= 0 ? ScopeBandBase + profileId : scope;
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

        private void EnqueueScopeDestroy(in InteractionContextInstance instance)
        {
            var command = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DestroyPresenterScope,
                RouteStrategy = PresenterCommandRouteStrategy.DestroyScope,
                ScopeTag = instance.ScopeTag,
                ScopeSource = PresenterCommandScopeSource.Fixed,
            };

            if (!_commands.TryAdd(in command))
            {
                throw new InvalidOperationException(
                    "PresenterCommandBuffer overflowed while destroying an interaction context instance scope.");
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
                PayloadA = instance.ScopeTag,
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
