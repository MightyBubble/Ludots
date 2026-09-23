using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using CameraAcceptanceMod.Runtime;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceProjectionClickSystem : ISystem<float>, IInputFrameConsumer
    {
        private const float PickRadiusPixels = 20f;

        private readonly CameraAcceptanceRuntime _runtime;

        public CameraAcceptanceProjectionClickSystem(CameraAcceptanceRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt) { }

        public void Consume(GameEngine engine, PlayerInputHandler input, float deltaTime)
        {
            if (!string.Equals(
                    engine.CurrentMapSession?.MapId.Value,
                    CameraAcceptanceIds.ProjectionMapId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(
                engine.GlobalContext,
                nameof(CameraAcceptanceProjectionClickSystem));
            if (!input.PressedThisFrame(bindings.ConfirmActionId))
            {
                return;
            }

            Vector2 pointer = input.ReadAction<Vector2>(bindings.PointerPositionActionId);
            if (!TryResolveProjectionGround(engine, pointer, out WorldCmInt2 worldCm))
            {
                throw new InvalidOperationException(
                    "Camera projection acceptance requires a resolvable ground point for the confirm click.");
            }

            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity owner) ||
                owner == Entity.Null ||
                !engine.World.IsAlive(owner))
            {
                throw new InvalidOperationException(
                    "Camera projection acceptance requires a live sole ClientLocalSeat possession for the confirm click.");
            }

            Entity selected = CommandSourcePointerHitResolver.FindNearestInspectableEntity(
                engine.World,
                engine.GlobalContext,
                owner,
                pointer,
                PickRadiusPixels);
            _runtime.HandleSelectionConfirmed(engine, worldCm, selected, owner);
        }

        private static bool TryResolveProjectionGround(GameEngine engine, Vector2 pointer, out WorldCmInt2 worldCm)
        {
            worldCm = default;
            var rayProvider = engine.GetService(CoreServiceKeys.ScreenRayProvider);
            if (rayProvider == null)
            {
                return false;
            }

            var bounds = engine.CurrentMapSession?.PrimaryBoard?.WorldSize.Bounds ?? engine.WorldSizeSpec.Bounds;
            return GroundRaycastUtil.TryGetGroundWorldCmBounded(
                rayProvider.GetRay(pointer),
                bounds,
                out worldCm,
                out _);
        }
    }
}
