using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using EastAsiaNavMeshDebugMod.Input;

namespace EastAsiaNavMeshDebugMod.Systems
{
    public sealed class EastAsiaNavWalkabilityTextureToggleSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly PlayerInputHandler _input;

        public EastAsiaNavWalkabilityTextureToggleSystem(
            GameEngine engine,
            PlayerInputHandler input)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (!_input.PressedThisFrame(EastAsiaNavMeshDebugInputActions.ToggleNavWalkabilityTexture))
            {
                return;
            }

            if (!_engine.TryGetService(CoreServiceKeys.RenderDebugState, out RenderDebugState? renderDebug) ||
                renderDebug == null)
            {
                throw new InvalidOperationException(
                    $"{CoreServiceKeys.RenderDebugState.Name} is required to toggle the nav walkability texture.");
            }

            renderDebug.DrawNavWalkabilityTexture = !renderDebug.DrawNavWalkabilityTexture;
            Console.WriteLine(
                $"[EastAsiaNavMeshDebug] nav walkability texture {(renderDebug.DrawNavWalkabilityTexture ? "enabled" : "disabled")}");
        }
    }
}
