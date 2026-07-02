using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.MovePlanning;
using Ludots.Core.Presentation.Events;
using RoadNetworkShowcaseMod.Gameplay;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadSelectedRoutePresentationSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly SelectionRuntime _selection;
        private readonly RoadRoutePreviewSplineBuilder _builder;
        private readonly RoadRouteProfileCatalog _profiles;
        private readonly PresentationWorldFactPublisher _facts;
        private readonly List<RoadRoutePreviewFactScope> _currentScopes = new();
        private readonly List<RoadRoutePreviewFactScope> _previousScopes = new();
        private readonly HashSet<RoadRoutePreviewFactScope> _currentScopeSet = new();

        public RoadSelectedRoutePresentationSystem(World world, Dictionary<string, object> globals, SelectionRuntime selection, MovePlanStore plans)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _builder = new RoadRoutePreviewSplineBuilder(plans ?? throw new ArgumentNullException(nameof(plans)));
            _profiles = new RoadRouteProfileCatalog(world);
            if (!PresentationWorldFactPublisher.TryCreate(globals, out _facts))
            {
                throw new InvalidOperationException("Road selected route presentation requires PresentationEventStream.");
            }
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            _currentScopes.Clear();
            _currentScopeSet.Clear();
            Entity[] selected = SelectionContextRuntime.SnapshotCurrentSelection(_world, _globals);
            int count = selected.Length;
            if (count <= 0)
            {
                EndPreviousScopes();
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Entity entity = selected[i];
                if (!_world.IsAlive(entity) || !_world.Has<OrderBuffer>(entity))
                {
                    continue;
                }

                ref var buffer = ref _world.Get<OrderBuffer>(entity);
                RoadRoutePreviewPalette palette = _profiles.ResolvePreviewPalette(entity);
                _builder.EmitSelectionPreview(_world, entity, ref buffer, in palette, _facts, _currentScopes);
            }

            RemoveStaleScopes();
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
            EndPreviousScopes();
        }

        private void RemoveStaleScopes()
        {
            for (int i = 0; i < _currentScopes.Count; i++)
            {
                _currentScopeSet.Add(_currentScopes[i]);
            }

            for (int i = 0; i < _previousScopes.Count; i++)
            {
                RoadRoutePreviewFactScope previous = _previousScopes[i];
                if (!_currentScopeSet.Contains(previous))
                {
                    EndScope(previous);
                }
            }

            _previousScopes.Clear();
            _previousScopes.AddRange(_currentScopes);
        }

        private void EndPreviousScopes()
        {
            for (int i = 0; i < _previousScopes.Count; i++)
            {
                EndScope(_previousScopes[i]);
            }

            _previousScopes.Clear();
            _currentScopes.Clear();
            _currentScopeSet.Clear();
        }

        private void EndScope(in RoadRoutePreviewFactScope scope)
        {
            if (scope.Key.Contains(".endpoint.", StringComparison.Ordinal))
            {
                _facts.PublishWorldOverlayEnded(scope.Key, Entity.Null, scope.Scope);
                return;
            }

            _facts.PublishWorldSplineEnded(scope.Key, Entity.Null, scope.Scope);
        }
    }
}
