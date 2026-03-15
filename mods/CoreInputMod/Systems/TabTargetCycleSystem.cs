using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    public sealed class TabTargetCycleSystem : ISystem<float>
    {
        public const string TabTargetActionId = "TabTarget";
        public const string TabTargetReverseActionId = "TabTargetReverse";

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly int _searchRadiusCm;
        private readonly Entity[] _candidateScratch = new Entity[64];
        private readonly float[] _distanceScratch = new float[64];
        private int _lastCycleIndex = -1;
        private static readonly QueryDescription CandidateQuery = new QueryDescription().WithAll<VisualTransform, SelectionSelectableTag>();

        public TabTargetCycleSystem(World world, Dictionary<string, object> globals, int searchRadiusCm = 3000)
        {
            _world = world;
            _globals = globals;
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

            if (!_world.IsAlive(local) || !_world.Has<VisualTransform>(local))
            {
                return;
            }

            int localTeam = _world.Has<Team>(local) ? _world.Get<Team>(local).Id : 0;
            Vector3 localPos = _world.Get<VisualTransform>(local).Position;
            float maxDistanceSq = _searchRadiusCm * _searchRadiusCm;

            int count = 0;

            _world.Query(in CandidateQuery, (Entity entity, ref VisualTransform transform, ref SelectionSelectableTag selectable) =>
            {
                if (count >= 64 || entity.Id == local.Id || !SelectionEligibility.IsSelectableNow(_world, entity))
                {
                    return;
                }

                if (_world.Has<Team>(entity))
                {
                    var entityTeam = _world.Get<Team>(entity).Id;
                    if (entityTeam == localTeam)
                    {
                        return;
                    }
                }

                Vector3 position = transform.Position;
                float dx = position.X - localPos.X;
                float dz = position.Z - localPos.Z;
                float distanceSq = dx * dx + dz * dz;
                if (distanceSq > maxDistanceSq)
                {
                    return;
                }

                int insertAt = count;
                while (insertAt > 0 && _distanceScratch[insertAt - 1] > distanceSq)
                {
                    insertAt--;
                }

                for (int j = count; j > insertAt; j--)
                {
                    _candidateScratch[j] = _candidateScratch[j - 1];
                    _distanceScratch[j] = _distanceScratch[j - 1];
                }

                _candidateScratch[insertAt] = entity;
                _distanceScratch[insertAt] = distanceSq;
                count++;
            });

            if (count == 0)
            {
                _globals.Remove(CoreServiceKeys.TabTargetEntity.Name);
                return;
            }

            int nextIndex = forward
                ? (_lastCycleIndex + 1) % count
                : (_lastCycleIndex <= 0 ? count - 1 : _lastCycleIndex - 1);

            _lastCycleIndex = nextIndex;
            _globals[CoreServiceKeys.TabTargetEntity.Name] = _candidateScratch[nextIndex];
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
