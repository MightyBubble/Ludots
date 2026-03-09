using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Generic click-selection system.
    /// Resolves hovered and selected entity state using the shared interaction bindings.
    /// When rich selection is active, this system keeps hover up to date but does not own selection.
    /// </summary>
    public sealed class EntityClickSelectSystem : ISystem<float>
    {
        private static readonly InteractionActionBindings DefaultBindings = new InteractionActionBindings();

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly ISpatialQueryService _spatial;

        public int PickRadiusCm { get; set; } = 120;
        public Action<WorldCmInt2, Entity>? OnEntitySelected { get; set; }

        public EntityClickSelectSystem(World world, Dictionary<string, object> globals, ISpatialQueryService spatial)
        {
            _world = world;
            _globals = globals;
            _spatial = spatial;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) || inputObj is not PlayerInputHandler input)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayObj) || rayObj is not IScreenRayProvider rayProvider)
            {
                return;
            }

            var bindings = ResolveBindings();
            var mouse = input.ReadAction<System.Numerics.Vector2>(bindings.PointerPositionActionId);
            var ray = rayProvider.GetRay(mouse);
            if (GroundRaycastUtil.TryGetGroundWorldCm(in ray, out var hoveredWorldCm))
            {
                var hovered = FindNearestEntity(hoveredWorldCm, PickRadiusCm);
                if (_world.IsAlive(hovered))
                {
                    _globals[CoreServiceKeys.HoveredEntity.Name] = hovered;
                }
                else
                {
                    _globals.Remove(CoreServiceKeys.HoveredEntity.Name);
                }
            }
            else
            {
                _globals.Remove(CoreServiceKeys.HoveredEntity.Name);
            }

            bool richSelectionActive = IsRichSelectionActive();
            if (!richSelectionActive
                && (!_globals.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var existingSelObj)
                    || existingSelObj is not Entity existingSel
                    || !_world.IsAlive(existingSel)))
            {
                if (_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj)
                    && localObj is Entity local
                    && _world.IsAlive(local))
                {
                    _globals[CoreServiceKeys.SelectedEntity.Name] = local;
                }
            }

            if (!input.PressedThisFrame(bindings.ConfirmActionId) || richSelectionActive)
            {
                return;
            }

            if (!GroundRaycastUtil.TryGetGroundWorldCm(in ray, out var worldCm))
            {
                return;
            }

            var selected = FindNearestEntity(worldCm, PickRadiusCm);
            _globals[CoreServiceKeys.SelectedEntity.Name] = selected;
            OnEntitySelected?.Invoke(worldCm, selected);
        }

        private bool IsRichSelectionActive()
        {
            return _globals.TryGetValue(CoreServiceKeys.ActiveSelectionProfileId.Name, out var profileObj)
                && profileObj is string profileId
                && !string.IsNullOrWhiteSpace(profileId)
                && _globals.TryGetValue(CoreServiceKeys.SelectionInputHandler.Name, out var handlerObj)
                && handlerObj is ISelectionInputHandler
                && _globals.TryGetValue(CoreServiceKeys.SelectionCandidatePolicy.Name, out var policyObj)
                && policyObj is ISelectionCandidatePolicy;
        }

        private Entity FindNearestEntity(in WorldCmInt2 worldCm, int radiusCm)
        {
            Span<Entity> buffer = stackalloc Entity[256];
            var result = _spatial.QueryRadius(worldCm, radiusCm, buffer);
            int count = result.Count;
            if (count <= 0)
            {
                return default;
            }

            Entity best = default;
            long bestD2 = long.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var entity = buffer[i];
                if (!_world.IsAlive(entity))
                {
                    continue;
                }

                ref var pos = ref _world.TryGetRef<WorldPositionCm>(entity, out bool hasPos);
                if (!hasPos)
                {
                    continue;
                }

                var cmPos = pos.Value.ToWorldCmInt2();
                long dx = cmPos.X - worldCm.X;
                long dy = cmPos.Y - worldCm.Y;
                long d2 = dx * dx + dy * dy;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = entity;
                }
            }

            return best;
        }

        private InteractionActionBindings ResolveBindings()
        {
            if (_globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var obj) && obj is InteractionActionBindings bindings)
            {
                return bindings;
            }

            return DefaultBindings;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
