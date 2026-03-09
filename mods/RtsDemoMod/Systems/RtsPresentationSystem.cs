using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using RtsDemoMod.Components;

namespace RtsDemoMod.Systems
{
    public sealed class RtsPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly DebugDrawCommandBuffer _debugDraw;
        private readonly int _cubeMeshId;
        private readonly int _sphereMeshId;

        private readonly QueryDescription _unitQuery = new QueryDescription().WithAll<RtsScenarioEntity, VisualTransform, Team>();
        private readonly QueryDescription _selectedQuery = new QueryDescription().WithAll<SelectedTag, VisualTransform>();
        private readonly QueryDescription _velocityQuery = new QueryDescription().WithAll<NavAgent2D, VisualTransform, NavDesiredVelocity2D>();

        public RtsPresentationSystem(GameEngine engine, DebugDrawCommandBuffer debugDraw, MeshAssetRegistry meshes)
        {
            _engine = engine;
            _world = engine.World;
            _globals = engine.GlobalContext;
            _debugDraw = debugDraw;
            _cubeMeshId = meshes.GetId(WellKnownMeshKeys.Cube);
            _sphereMeshId = meshes.GetId(WellKnownMeshKeys.Sphere);
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!IsActive())
            {
                _debugDraw.Clear();
                return;
            }

            _debugDraw.Clear();
            AppendUnits();
            AppendSelectionCircles();
            AppendVelocityArrows();
            AppendCommandMarker();
            AppendOverlay();
        }

        private void AppendUnits()
        {
            if (!_globals.TryGetValue(CoreServiceKeys.PresentationPrimitiveDrawBuffer.Name, out var drawObj) || drawObj is not PrimitiveDrawBuffer draw)
            {
                return;
            }

            foreach (ref var chunk in _world.Query(in _unitQuery))
            {
                var visuals = chunk.GetSpan<VisualTransform>();
                var teams = chunk.GetSpan<Team>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = chunk.Entity(i);
                    ref var visual = ref visuals[i];
                    bool isBlocker = teams[i].Id == 0;
                    var spec = RtsUnitRuntimeSetup.GetVisualSpec(_world, entity);
                    float diameterMeters = spec.DiameterCm * 0.01f;
                    float heightMeters = spec.HeightCm * 0.01f;
                    if (diameterMeters <= 0f)
                    {
                        continue;
                    }

                    var color = ResolveTeamColor(teams[i].Id, _world.Has<SelectedTag>(entity));
                    draw.TryAdd(new PrimitiveDrawItem
                    {
                        MeshAssetId = isBlocker ? _cubeMeshId : _sphereMeshId,
                        Position = new Vector3(visual.Position.X, (isBlocker ? heightMeters : diameterMeters) * 0.5f, visual.Position.Z),
                        Scale = isBlocker
                            ? new Vector3(diameterMeters, Math.Max(heightMeters, diameterMeters), diameterMeters)
                            : new Vector3(diameterMeters, diameterMeters, diameterMeters),
                        Color = color
                    });
                }
            }
        }

        private void AppendSelectionCircles()
        {
            foreach (ref var chunk in _world.Query(in _selectedQuery))
            {
                var visuals = chunk.GetSpan<VisualTransform>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = chunk.Entity(i);
                    var visualSpec = RtsUnitRuntimeSetup.GetVisualSpec(_world, entity);
                    float navRadiusMeters = RtsUnitRuntimeSetup.GetRadiusCm(_world, entity) * 0.01f;
                    float visualRadiusMeters = visualSpec.DiameterCm * 0.005f;
                    float radiusMeters = MathF.Max(navRadiusMeters, visualRadiusMeters) + 0.08f;
                    _debugDraw.Circles.Add(new DebugDrawCircle2D
                    {
                        Center = new Vector2(visuals[i].Position.X, visuals[i].Position.Z),
                        Radius = radiusMeters,
                        Thickness = 1f,
                        Color = DebugDrawColor.Cyan
                    });
                }
            }
        }

        private void AppendVelocityArrows()
        {
            var state = ResolveState();
            if (!state.ShowVelocityVectors)
            {
                return;
            }

            foreach (ref var chunk in _world.Query(in _velocityQuery))
            {
                var visuals = chunk.GetSpan<VisualTransform>();
                var desired = chunk.GetSpan<NavDesiredVelocity2D>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = chunk.Entity(i);
                    if (!_world.Has<SelectedTag>(entity) && (entity.Id % 3) != 0)
                    {
                        continue;
                    }

                    Vector2 from = new Vector2(visuals[i].Position.X, visuals[i].Position.Z);
                    Vector2 delta = desired[i].ValueCmPerSec.ToVector2() * 0.0045f;
                    if (delta.LengthSquared() < 0.0001f)
                    {
                        continue;
                    }

                    _debugDraw.Lines.Add(new DebugDrawLine2D
                    {
                        A = from,
                        B = from + delta,
                        Thickness = 1f,
                        Color = _world.Has<SelectedTag>(entity) ? DebugDrawColor.Yellow : DebugDrawColor.Green
                    });
                }
            }
        }

        private void AppendCommandMarker()
        {
            var state = ResolveState();
            if (!state.HasLastCommand)
            {
                return;
            }

            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = new Vector2(state.LastCommandPointCm.X * 0.01f, state.LastCommandPointCm.Y * 0.01f),
                Radius = 0.75f,
                Thickness = 1f,
                Color = DebugDrawColor.White
            });
        }

        private void AppendOverlay()
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) || overlayObj is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            var state = ResolveState();
            var selectionInteraction = ResolveSelectionInteractionState();
            var selected = new List<Entity>(64);
            int selectedCount = SelectionRuntime.CollectSelected(_world, _globals, selected);
            string activeViewMode = _globals.TryGetValue(ViewModeManager.ActiveModeIdKey, out var viewModeObj) && viewModeObj is string modeId
                ? modeId
                : "(none)";
            float cameraDistanceCm = _engine.GameSession.Camera.State.DistanceCm;
            string interactionMode = ResolveInteractionModeLabel();

            overlay.AddRect(14, 14, 640, 168, new Vector4(0.02f, 0.04f, 0.06f, 0.72f), new Vector4(0.35f, 0.75f, 1f, 0.4f));
            overlay.AddText(24, 24, $"RTS Demo | Scenario={state.CurrentScenario} | Selected={selectedCount}", 18, new Vector4(0.95f, 0.98f, 1f, 1f));
            overlay.AddText(24, 50, "LMB Click/Drag Select | RMB Move | S Stop | Shift Queue/Add", 15, new Vector4(0.82f, 0.9f, 0.96f, 1f));
            overlay.AddText(24, 72, "1 PassThrough | 2 Bottleneck | 3 Formation | 4 Toggle Vectors", 15, new Vector4(0.82f, 0.9f, 0.96f, 1f));
            overlay.AddText(24, 94, state.ShowVelocityVectors ? "Velocity vectors: ON" : "Velocity vectors: OFF", 15, new Vector4(1f, 0.92f, 0.64f, 1f));
            overlay.AddText(24, 116, $"ViewMode={activeViewMode} | OrderMode={interactionMode} | CameraDist={cameraDistanceCm:F0}cm", 15, new Vector4(0.78f, 0.9f, 0.78f, 1f));
            overlay.AddText(24, 138, "Mesh size follows RtsVisualDiameterCm/RtsVisualHeightCm, fallback NavRadiusCm", 14, new Vector4(0.78f, 0.9f, 0.78f, 1f));

            if (selectionInteraction.IsDragging && selectionInteraction.PreviewShape == SelectionPreviewShapeKind.Rectangle)
            {
                Vector2 min = Vector2.Min(selectionInteraction.AnchorScreen, selectionInteraction.CurrentScreen);
                Vector2 max = Vector2.Max(selectionInteraction.AnchorScreen, selectionInteraction.CurrentScreen);
                overlay.AddRect((int)min.X, (int)min.Y, Math.Max(1, (int)(max.X - min.X)), Math.Max(1, (int)(max.Y - min.Y)), new Vector4(0.1f, 0.4f, 0.9f, 0.15f), new Vector4(0.35f, 0.75f, 1f, 0.85f));
            }
        }

        private SelectionInteractionState ResolveSelectionInteractionState()
        {
            if (_globals.TryGetValue(CoreServiceKeys.SelectionInteractionState.Name, out var obj) && obj is SelectionInteractionState state)
            {
                return state;
            }

            state = new SelectionInteractionState();
            _globals[CoreServiceKeys.SelectionInteractionState.Name] = state;
            return state;
        }
        private string ResolveInteractionModeLabel()
        {
            if (_globals.TryGetValue(LocalOrderSourceHelper.ActiveMappingKey, out var mappingObj) && mappingObj is InputOrderMappingSystem mapping)
            {
                return mapping.InteractionMode.ToString();
            }

            return "(pending)";
        }

        private static Vector4 ResolveTeamColor(int teamId, bool selected)
        {
            Vector4 color = teamId switch
            {
                1 => new Vector4(0.18f, 0.92f, 0.35f, 1f),
                2 => new Vector4(0.95f, 0.24f, 0.22f, 1f),
                3 => new Vector4(0.95f, 0.82f, 0.2f, 1f),
                _ => new Vector4(0.65f, 0.65f, 0.68f, 1f),
            };

            if (selected)
            {
                color.X = MathF.Min(1f, color.X + 0.18f);
                color.Y = MathF.Min(1f, color.Y + 0.12f);
                color.Z = MathF.Min(1f, color.Z + 0.18f);
            }

            return color;
        }

        private bool IsActive()
        {
            return _globals.TryGetValue(RtsDemoKeys.IsActiveMap, out var obj) && obj is bool enabled && enabled;
        }

        private RtsSelectionState ResolveState()
        {
            if (_globals.TryGetValue(RtsDemoKeys.SelectionState, out var obj) && obj is RtsSelectionState state)
            {
                return state;
            }

            state = new RtsSelectionState();
            _globals[RtsDemoKeys.SelectionState] = state;
            return state;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}







