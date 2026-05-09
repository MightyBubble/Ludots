using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Runtime
{
    /// <summary>
    /// Captures screen-pointer ground projection during the visual input frame so
    /// fixed-step systems can consume a stable authoritative world point.
    /// </summary>
    public static class AuthoritativeGroundPointerHelper
    {
        public const string ActionId = "__runtime.PointerGroundWorldCm";

        public static void Capture(
            IReadOnlyDictionary<string, object> globals,
            PlayerInputHandler input,
            AuthoritativeInputAccumulator accumulator)
        {
            if (globals == null) throw new ArgumentNullException(nameof(globals));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (accumulator == null) throw new ArgumentNullException(nameof(accumulator));

            InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(globals, nameof(AuthoritativeGroundPointerHelper));
            if (TryConsumeOverride(globals, bindings.CommandActionId, out WorldCmInt2 overrideWorldCm))
            {
                accumulator.CaptureAction(
                    ActionId,
                    new Vector3(overrideWorldCm.X, 0f, overrideWorldCm.Y),
                    isDown: true,
                    pressedThisFrame: false,
                    releasedThisFrame: false);
                return;
            }

            if (!TryResolveFromScreen(globals, input.ReadAction<Vector2>(bindings.PointerPositionActionId), out WorldCmInt2 worldCm))
            {
                accumulator.CaptureAction(ActionId, Vector3.Zero, isDown: false, pressedThisFrame: false, releasedThisFrame: false);
                return;
            }

            accumulator.CaptureAction(
                ActionId,
                new Vector3(worldCm.X, 0f, worldCm.Y),
                isDown: true,
                pressedThisFrame: false,
                releasedThisFrame: false);
        }

        public static bool TryConsumeOverride(
            IReadOnlyDictionary<string, object> globals,
            string actionId,
            out WorldCmInt2 worldCm)
        {
            worldCm = default;
            return globals.TryGetValue(CoreServiceKeys.AuthoritativeGroundPointerOverride.Name, out var overrideObj) &&
                overrideObj is AuthoritativeGroundPointerOverride pointerOverride &&
                pointerOverride.TryConsume(actionId, out worldCm);
        }

        public static bool TryRead(IInputActionReader input, out WorldCmInt2 worldCm)
        {
            worldCm = default;
            if (input == null || !input.IsDown(ActionId))
            {
                return false;
            }

            Vector3 value = input.ReadAction<Vector3>(ActionId);
            worldCm = new WorldCmInt2(
                (int)MathF.Round(value.X, MidpointRounding.AwayFromZero),
                (int)MathF.Round(value.Z, MidpointRounding.AwayFromZero));
            return true;
        }

        public static bool TryResolveFromScreen(
            IReadOnlyDictionary<string, object> globals,
            Vector2 screenPosition,
            out WorldCmInt2 worldCm)
        {
            worldCm = default;
            if (!globals.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayProviderObj) ||
                rayProviderObj is not IScreenRayProvider rayProvider ||
                !globals.TryGetValue(CoreServiceKeys.VisualHeightmap.Name, out var heightmapObj) ||
                heightmapObj is not IVisualHeightmap heightmap ||
                !globals.TryGetValue(CoreServiceKeys.WorldSizeSpec.Name, out var worldSizeObj) ||
                worldSizeObj is not WorldSizeSpec worldSize)
            {
                return false;
            }

            try
            {
                ScreenRay ray = rayProvider.GetRay(screenPosition);
                return GroundRaycastUtil.TryGetGroundWorldCmBounded(in ray, heightmap, worldSize, out worldCm);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }
}
