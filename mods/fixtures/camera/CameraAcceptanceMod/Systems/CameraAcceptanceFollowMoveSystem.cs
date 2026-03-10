using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceFollowMoveSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly List<Entity> _selected = new(SelectionBuffer.CAPACITY);

        public CameraAcceptanceFollowMoveSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, CameraAcceptanceIds.FollowMapId, StringComparison.OrdinalIgnoreCase))
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
                inputObj is not IInputActionReader authoritativeInput)
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.ScreenRayProvider) is not IScreenRayProvider rayProvider)
            {
                throw new InvalidOperationException("ScreenRayProvider is required for follow-move acceptance.");
            }

            if (!authoritativeInput.PressedThisFrame("Command"))
            {
                return;
            }

            if (SelectionRuntime.CollectSelected(_engine.World, _engine.GlobalContext, _selected) <= 0)
            {
                return;
            }

            var pointer = authoritativeInput.ReadAction<Vector2>("PointerPos");
            var ray = rayProvider.GetRay(pointer);
            if (!GroundRaycastUtil.TryGetGroundWorldCm(in ray, out var groundCm))
            {
                return;
            }

            var target = new Vector2(groundCm.X, groundCm.Y);
            if (_selected.Count == 1)
            {
                SetPosition(_selected[0], target);
                return;
            }

            Vector2 centroid = Vector2.Zero;
            int count = 0;
            for (int i = 0; i < _selected.Count; i++)
            {
                var entity = _selected[i];
                if (!_engine.World.IsAlive(entity) || !_engine.World.Has<WorldPositionCm>(entity))
                {
                    continue;
                }

                centroid += _engine.World.Get<WorldPositionCm>(entity).Value.ToVector2();
                count++;
            }

            if (count <= 0)
            {
                return;
            }

            centroid /= count;
            var delta = target - centroid;
            for (int i = 0; i < _selected.Count; i++)
            {
                var entity = _selected[i];
                if (!_engine.World.IsAlive(entity) || !_engine.World.Has<WorldPositionCm>(entity))
                {
                    continue;
                }

                var next = _engine.World.Get<WorldPositionCm>(entity).Value.ToVector2() + delta;
                SetPosition(entity, next);
            }
        }

        private void SetPosition(Entity entity, Vector2 target)
        {
            _engine.World.Set(entity, WorldPositionCm.FromCm((int)target.X, (int)target.Y));
        }
    }
}
