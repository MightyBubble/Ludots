using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Systems
{
    /// <summary>
    /// Opens a published pointer-region context on the subjects that own interaction
    /// state. Runs after seat possession and before the trigger mount gate, so the
    /// new context's triggers are mounted in the same tick as the entering pointer.
    /// Leaving the rectangle does not close the context: the mounted graphs own
    /// sticky drags. A UI capture closes every published region context.
    /// </summary>
    public sealed class PointerRegionContextSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly PointerRegionContextRegistry _regions;
        private readonly InteractionContextInstanceRuntime _contexts;
        private readonly InteractionContextProfileRegistry _profiles;
        private readonly List<Entity> _subjects = new();
        private readonly List<string> _contextIds = new();
        private readonly QueryDescription _contextQuery = new QueryDescription().WithAll<InteractionContextInstance>();

        public PointerRegionContextSystem(
            World world,
            Dictionary<string, object> globals,
            PointerRegionContextRegistry regions,
            InteractionContextInstanceRuntime contexts,
            InteractionContextProfileRegistry profiles)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _regions = regions ?? throw new ArgumentNullException(nameof(regions));
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        }

        public void Initialize()
        {
        }

        public void Update(in float dt)
        {
            if (_regions.Count == 0)
            {
                return;
            }

            CollectSubjects();
            if (_subjects.Count == 0)
            {
                return;
            }

            if (UiCaptured())
            {
                DeactivatePublished();
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object? inputObj) ||
                inputObj is not IInputActionReader input)
            {
                throw new InvalidOperationException(
                    "POINTER_REGION.ERR.InputMissing: AuthoritativeInput is required while pointer regions are published.");
            }

            if (!input.TryReadPresentVector2(ReservedInputActionIds.PointerPos, out Vector2 pointer))
            {
                return;
            }

            if (!_regions.TryHit(pointer, out string contextId))
            {
                return;
            }

            Activate(contextId);
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private bool UiCaptured()
        {
            return _globals.TryGetValue(CoreServiceKeys.UiCaptured.Name, out object? capturedObj) &&
                capturedObj is bool captured &&
                captured;
        }

        private void CollectSubjects()
        {
            _subjects.Clear();
            if (!_globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? seatsObj) ||
                seatsObj is not ClientLocalSeatRegistry seats)
            {
                throw new InvalidOperationException(
                    "POINTER_REGION.ERR.SeatsMissing: ClientLocalSeatRegistry is required while pointer regions are published.");
            }

            if (seats.Count > 0)
            {
                IReadOnlyList<string> seatIds = seats.SeatIds;
                for (int i = 0; i < seatIds.Count; i++)
                {
                    ClientLocalSeat seat = seats.Require(seatIds[i]);
                    if (!seat.HasPossession || !_world.IsAlive(seat.PossessedRep))
                    {
                        continue;
                    }

                    if (_world.Has<InteractionContextInstance>(seat.PossessedRep))
                    {
                        _subjects.Add(seat.PossessedRep);
                    }
                }

                return;
            }

            _world.Query(in _contextQuery, (Entity entity, ref InteractionContextInstance _) =>
            {
                _subjects.Add(entity);
            });
        }

        private void Activate(string contextId)
        {
            int profileId = RequireProfileId(contextId);
            int contextKeyId = _contexts.RequireConfigKeyId(profileId);
            for (int i = 0; i < _subjects.Count; i++)
            {
                Entity subject = _subjects[i];
                if (_contexts.IsActive(subject, profileId))
                {
                    continue;
                }

                int parentKeyId = 0;
                Span<int> active = stackalloc int[8];
                int count = InteractionContextInstanceRuntime.CopyActiveContextIdsNewestFirst(_world, subject, active);
                if (count > 0)
                {
                    parentKeyId = _contexts.RequireConfigKeyId(active[0]);
                }

                _contexts.Activate(subject, contextKeyId, parentKeyId);
            }
        }

        private void DeactivatePublished()
        {
            _regions.CopyContextIds(_contextIds);
            for (int c = 0; c < _contextIds.Count; c++)
            {
                int profileId = RequireProfileId(_contextIds[c]);
                int contextKeyId = _contexts.RequireConfigKeyId(profileId);
                for (int i = 0; i < _subjects.Count; i++)
                {
                    Entity subject = _subjects[i];
                    if (!_world.TryGet<InteractionContextInstances>(subject, out InteractionContextInstances instances) ||
                        instances.IndexOf(profileId) < 0)
                    {
                        continue;
                    }

                    _contexts.Deactivate(subject, contextKeyId);
                }
            }
        }

        private int RequireProfileId(string contextId)
        {
            if (!_profiles.ProfileIdRegistry.TryGetId(contextId, out int profileId) ||
                !_profiles.IsInstalled(profileId))
            {
                throw new InvalidOperationException(
                    $"POINTER_REGION.ERR.UnknownContext: '{contextId}' is not an installed interaction context profile.");
            }

            return profileId;
        }
    }
}
