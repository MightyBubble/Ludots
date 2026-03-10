using System;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceProjectionSpawnSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private int _fixtureSequence = 1;

        public CameraAcceptanceProjectionSpawnSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, CameraAcceptanceIds.ProjectionMapId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.UiCaptured.Name, out var capturedObj) &&
                capturedObj is bool captured &&
                captured)
            {
                return;
            }

            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
                inputObj is not IInputActionReader input ||
                !input.PressedThisFrame("Select") ||
                !input.IsDown(CameraAcceptanceIds.SpawnModifierActionId))
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.RuntimeEntityTemplateSpawner) is not RuntimeEntityTemplateSpawner spawner)
            {
                throw new InvalidOperationException("RuntimeEntityTemplateSpawner is required for projection fixture spawning.");
            }

            if (_engine.GetService(CoreServiceKeys.ScreenRayProvider) is not IScreenRayProvider rayProvider)
            {
                throw new InvalidOperationException("ScreenRayProvider is required for projection fixture spawning.");
            }

            var pointer = input.ReadAction<System.Numerics.Vector2>("PointerPos");
            var ray = rayProvider.GetRay(pointer);
            if (!GroundRaycastUtil.TryGetGroundWorldCm(in ray, out var worldCm))
            {
                return;
            }

            var entity = spawner.Spawn(CameraAcceptanceIds.FixtureTemplateId, (world, spawned) =>
            {
                var position = WorldPositionCm.FromCm(worldCm.X, worldCm.Y);
                if (world.Has<WorldPositionCm>(spawned))
                {
                    world.Set(spawned, position);
                }
                else
                {
                    world.Add(spawned, position);
                }

                var name = new Name
                {
                    Value = $"{CameraAcceptanceIds.FixtureNamePrefix}{_fixtureSequence++}"
                };
                if (world.Has<Name>(spawned))
                {
                    world.Set(spawned, name);
                }
                else
                {
                    world.Add(spawned, name);
                }

                if (!world.Has<CameraAcceptanceFixtureTag>(spawned))
                {
                    world.Add(spawned, new CameraAcceptanceFixtureTag());
                }
            });

            if (!_engine.World.IsAlive(entity))
            {
                throw new InvalidOperationException("Projection fixture spawn failed.");
            }
        }
    }
}
