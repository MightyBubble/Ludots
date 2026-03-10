using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;

namespace CoreInputMod.Systems
{
    public sealed class TabTargetCycleSystem : ISystem<float>
    {
        public const string TabTargetActionId = "TabTarget";
        public const string TabTargetReverseActionId = "TabTargetReverse";

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly ISpatialQueryService _spatial;
        private readonly int _searchRadiusCm;
        private int _lastCycleIndex = -1;

        public TabTargetCycleSystem(World world, Dictionary<string, object> globals, ISpatialQueryService spatial, int searchRadiusCm = 3000)
        {
            _world = world;
            _globals = globals;
            _spatial = spatial;
            _searchRadiusCm = searchRadiusCm;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) || inputObj is not IInputActionReader input)
            {
                return;
            }

            bool forward = input.PressedThisFrame(TabTargetActionId);
            bool reverse = input.PressedThisFrame(TabTargetReverseActionId);
            if (!forward && !reverse)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) || localObj is not Entity local)
            {
                return;
            }

            if (!_world.IsAlive(local) || !_world.Has<WorldPositionCm>(local))
            {
                return;
            }

            int localTeam = _world.Has<Team>(local) ? _world.Get<Team>(local).Id : 0;
            var localPos = _world.Get<WorldPositionCm>(local).Value.ToWorldCmInt2();

            Span<Entity> raw = stackalloc Entity[256];
            var result = _spatial.QueryRadius(localPos, _searchRadiusCm, raw);

            Span<Entity> candidates = stackalloc Entity[64];
            Span<long> distances = stackalloc long[64];
            int count = 0;

            for (int i = 0; i < result.Count && count < 64; i++)
            {
                var entity = raw[i];
                if (!_world.IsAlive(entity) || entity.Id == local.Id || !_world.Has<WorldPositionCm>(entity))
                {
                    continue;
                }

                if (_world.Has<Team>(entity))
                {
                    var entityTeam = _world.Get<Team>(entity).Id;
                    if (!RelationshipFilterUtil.Passes(RelationshipFilter.Hostile, localTeam, entityTeam))
                    {
                        continue;
                    }
                }

                var position = _world.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2();
                long dx = position.X - localPos.X;
                long dy = position.Y - localPos.Y;
                long distanceSq = dx * dx + dy * dy;

                int insertAt = count;
                while (insertAt > 0 && distances[insertAt - 1] > distanceSq)
                {
                    insertAt--;
                }

                for (int j = count; j > insertAt; j--)
                {
                    candidates[j] = candidates[j - 1];
                    distances[j] = distances[j - 1];
                }

                candidates[insertAt] = entity;
                distances[insertAt] = distanceSq;
                count++;
            }

            if (count == 0)
            {
                return;
            }

            int nextIndex = forward
                ? (_lastCycleIndex + 1) % count
                : (_lastCycleIndex <= 0 ? count - 1 : _lastCycleIndex - 1);

            _lastCycleIndex = nextIndex;
            SelectionRuntime.EnsureControllerBuffers(_world, local);

            var selection = _world.Get<SelectionBuffer>(local);
            for (int i = 0; i < selection.Count; i++)
            {
                var selected = selection.Get(i);
                if (_world.IsAlive(selected) && _world.Has<SelectedTag>(selected))
                {
                    _world.Remove<SelectedTag>(selected);
                }
            }

            selection.Clear();
            selection.Add(candidates[nextIndex]);
            _world.Set(local, selection);
            if (!_world.Has<SelectedTag>(candidates[nextIndex]))
            {
                _world.Add(candidates[nextIndex], new SelectedTag());
            }

            SelectionRuntime.SyncSelectedEntity(_world, _globals, in selection);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
