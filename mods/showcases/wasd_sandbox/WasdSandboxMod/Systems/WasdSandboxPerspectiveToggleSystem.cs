using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace WasdSandboxMod.Systems
{
    /// <summary>
    /// Cycles the sole seat's possession across every alive rep that declares a
    /// <see cref="CameraProfileBinding"/>. The camera profile switch itself is not this system's
    /// job — <see cref="Ludots.Core.Systems.CameraProfileBindingSystem"/> adopts the binding of
    /// whichever rep becomes possessed.
    /// </summary>
    public sealed class WasdSandboxPerspectiveToggleSystem : ISystem<float>
    {
        public const string PerspectiveToggleActionId = "PerspectiveToggle";

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly List<Entity> _candidates = new();
        private static readonly QueryDescription BindingQuery = new QueryDescription().WithAll<CameraProfileBinding, WorldPositionCm>();

        public WasdSandboxPerspectiveToggleSystem(World world, Dictionary<string, object> globals)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object? inputObj) ||
                inputObj is not IInputActionReader input ||
                !input.PressedThisFrame(PerspectiveToggleActionId))
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? seatsObj) ||
                seatsObj is not ClientLocalSeatRegistry seats ||
                seats.Count != 1 ||
                !seats.TryGetSoleSeat(out ClientLocalSeat seat))
            {
                return;
            }

            _candidates.Clear();
            _world.Query(in BindingQuery, (Entity entity) => _candidates.Add(entity));
            if (_candidates.Count < 2)
            {
                return;
            }

            Entity next = Entity.Null;
            for (int i = 0; i < _candidates.Count; i++)
            {
                if (_candidates[i] == seat.PossessedRep)
                {
                    next = _candidates[(i + 1) % _candidates.Count];
                    break;
                }
            }

            if (next == Entity.Null)
            {
                next = _candidates[0];
            }

            seats.SetPossession(seat.SeatId, seat.PossessedPlayerId, next);
        }
    }
}
