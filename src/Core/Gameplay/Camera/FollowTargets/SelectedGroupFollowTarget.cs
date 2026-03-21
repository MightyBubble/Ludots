using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Camera.FollowTargets
{
    public sealed class SelectedGroupFollowTarget : ICameraFollowTarget
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly SelectionRuntime? _selection;
        private Entity[] _scratch = new Entity[16];

        public SelectedGroupFollowTarget(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _selection = globals.TryGetValue(CoreServiceKeys.SelectionRuntime.Name, out var selectionObj) &&
                         selectionObj is SelectionRuntime selection
                ? selection
                : null;
        }

        public bool TryGetPosition(out Vector2 positionCm)
        {
            return TryGetSelectionCentroid(out positionCm);
        }

        private bool TryGetSelectionCentroid(out Vector2 positionCm)
        {
            positionCm = default;
            if (_selection == null)
            {
                return false;
            }

            int count = SelectionViewRuntime.GetViewedSelectionCount(_world, _globals, _selection);
            if (count <= 0)
            {
                return false;
            }

            EnsureScratchCapacity(count);
            count = SelectionViewRuntime.CopyViewedSelection(_world, _globals, _selection, _scratch);

            Vector2 weightedSum = Vector2.Zero;
            float totalWeight = 0f;

            for (int i = 0; i < count; i++)
            {
                Entity entity = _scratch[i];
                if (!_world.IsAlive(entity) || !_world.Has<WorldPositionCm>(entity))
                {
                    continue;
                }

                Vector2 entityPosition = _world.Get<WorldPositionCm>(entity).Value.ToVector2();
                float weight = ResolveWeight(entity);
                weightedSum += entityPosition * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f)
            {
                return false;
            }

            positionCm = weightedSum / totalWeight;
            return true;
        }

        private float ResolveWeight(Entity entity)
        {
            if (_world.Has<CameraFollowWeight>(entity))
            {
                float configured = _world.Get<CameraFollowWeight>(entity).Value;
                if (configured > 0f)
                {
                    return configured;
                }
            }

            return 1f;
        }

        private void EnsureScratchCapacity(int required)
        {
            if (required <= _scratch.Length)
            {
                return;
            }

            int next = _scratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _scratch, next);
        }
    }
}
