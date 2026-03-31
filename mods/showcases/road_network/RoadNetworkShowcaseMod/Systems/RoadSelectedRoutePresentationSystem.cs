using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Gameplay;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadSelectedRoutePresentationSystem : ISystem<float>
    {
        private const int SelectionScratchCapacity = 32;
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly SelectionRuntime _selection;
        private readonly Entity[] _selected = new Entity[SelectionScratchCapacity];
        private readonly RoadRoutePreviewSplineBuilder _builder;
        private readonly RoadRouteProfileCatalog _profiles;

        public RoadSelectedRoutePresentationSystem(World world, Dictionary<string, object> globals, SelectionRuntime selection, RoadNavPlanStore plans)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _builder = new RoadRoutePreviewSplineBuilder(plans ?? throw new ArgumentNullException(nameof(plans)));
            _profiles = new RoadRouteProfileCatalog(world);
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!TryGetBuffers(out RoadSplineBuffer? roadSplines, out GroundOverlayBuffer? overlays))
            {
                return;
            }

            int count = SelectionViewRuntime.CopyViewedSelection(_world, _globals, _selection, _selected);
            if (count <= 0)
            {
                return;
            }

            int stableBase = 20000;
            for (int i = 0; i < count; i++)
            {
                Entity entity = _selected[i];
                if (!_world.IsAlive(entity) || !_world.Has<OrderBuffer>(entity))
                {
                    continue;
                }

                ref var buffer = ref _world.Get<OrderBuffer>(entity);
                RoadRoutePreviewPalette palette = _profiles.ResolvePreviewPalette(entity);
                _builder.EmitSelectionPreview(_world, entity, ref buffer, in palette, roadSplines!, overlays!, stableBase + (i * 128));
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private bool TryGetBuffers(out RoadSplineBuffer? roadSplines, out GroundOverlayBuffer? overlays)
        {
            roadSplines = null;
            overlays = null;
            if (!_globals.TryGetValue(CoreServiceKeys.RoadSplineBuffer.Name, out object? roadSplineObj) ||
                roadSplineObj is not RoadSplineBuffer resolvedRoadSplines ||
                !_globals.TryGetValue(CoreServiceKeys.GroundOverlayBuffer.Name, out object? overlaysObj) ||
                overlaysObj is not GroundOverlayBuffer resolvedOverlays)
            {
                return false;
            }

            roadSplines = resolvedRoadSplines;
            overlays = resolvedOverlays;
            return true;
        }
    }
}
