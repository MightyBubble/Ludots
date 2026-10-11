using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace CameraShowcaseMod.Systems
{
    /// <summary>
    /// Polls the showcase-bound mode keys (CameraShowcase.Controls context, pushed while any
    /// showcase map is focused): F1-F3 switch between the shared camera profiles, F4 re-requests
    /// the command-source follow camera on the sole possessed rep. Runs in LocalInput so
    /// replicated clients (which execute only that group) keep the mode keys alive.
    /// </summary>
    public sealed class CameraShowcaseCommandFollowSystem : ISystem<float>
    {
        private readonly Dictionary<string, object> _globals;

        public CameraShowcaseCommandFollowSystem(Dictionary<string, object> globals)
        {
            _globals = globals;
        }

        public void Initialize() { }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
                inputObj is not IInputActionReader input ||
                !_globals.TryGetValue(CoreServiceKeys.Engine.Name, out var engineObj) ||
                engineObj is not GameEngine engine)
            {
                return;
            }

            if (input.PressedThisFrame(CameraShowcaseIds.TacticalModeActionId))
            {
                Runtime.CameraShowcaseCameras.SwitchToProfile(engine, CameraShowcaseIds.TacticalProfileId);
            }
            else if (input.PressedThisFrame(CameraShowcaseIds.FollowModeActionId))
            {
                Runtime.CameraShowcaseCameras.SwitchToProfile(engine, CameraShowcaseIds.FollowProfileId);
            }
            else if (input.PressedThisFrame(CameraShowcaseIds.InspectModeActionId))
            {
                Runtime.CameraShowcaseCameras.SwitchToProfile(engine, CameraShowcaseIds.InspectProfileId);
            }
            else if (input.PressedThisFrame(CameraShowcaseIds.CommandSourceFollowModeActionId))
            {
                if (ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity owner) && owner != Entity.Null)
                {
                    Runtime.CameraShowcaseCameras.RequestCollectionFollowCamera(
                        engine, CameraShowcaseIds.CommandSourceFollowProfileId, owner, blendDurationSeconds: null);
                }
            }
        }
    }
}
