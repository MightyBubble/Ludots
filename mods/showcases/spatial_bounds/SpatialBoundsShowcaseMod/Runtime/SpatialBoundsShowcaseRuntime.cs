using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using SpatialBoundsShowcaseMod.UI;
using Ludots.Tests.TestCommon;

namespace SpatialBoundsShowcaseMod.Runtime
{
    internal sealed class SpatialBoundsShowcaseRuntime
    {
        private static readonly Vector4 PanelFill = new(0.05f, 0.08f, 0.11f, 0.88f);
        private static readonly Vector4 PanelBorder = new(0.31f, 0.76f, 0.89f, 0.95f);
        private static readonly Vector4 TitleColor = new(0.95f, 0.87f, 0.53f, 1f);
        private static readonly Vector4 TextColor = new(0.91f, 0.95f, 0.98f, 1f);
        private static readonly Vector4 HintColor = new(0.71f, 0.80f, 0.88f, 1f);
        private static readonly Vector4 AccentColor = new(0.52f, 0.85f, 0.76f, 1f);
        private static readonly Vector4 HoverFill = new(0.16f, 0.74f, 0.96f, 0.12f);
        private static readonly Vector4 HoverBorder = new(0.36f, 0.88f, 1f, 0.98f);
        private static readonly Vector4 SelectedFill = new(0.18f, 0.85f, 0.42f, 0.12f);
        private static readonly Vector4 SelectedBorder = new(0.45f, 1f, 0.62f, 0.98f);
        private static readonly Vector4 PrimaryFill = new(0.98f, 0.75f, 0.22f, 0.16f);
        private static readonly Vector4 PrimaryBorder = new(1f, 0.9f, 0.42f, 0.98f);
        private static readonly Vector4 FootprintStroke = new(0.94f, 0.54f, 0.28f, 0.92f);
        private static readonly Vector4 FootprintVertex = new(1f, 0.84f, 0.42f, 0.98f);
        private Entity _selectionOwner = Entity.Null;
        private bool _ownsSelectionOwner;
        private readonly SpatialBoundsShowcasePanelController _panelController;

        public SpatialBoundsShowcaseRuntime()
        {
            _panelController = new SpatialBoundsShowcasePanelController(this);
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            if (!SpatialBoundsShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                Disable(engine);
                return Task.CompletedTask;
            }

            EnsureSelectionContext(engine);
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            if (SpatialBoundsShowcaseIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
            {
                Disable(engine);
            }

            return Task.CompletedTask;
        }

        public void Update(GameEngine engine)
        {
            if (!SpatialBoundsShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                Disable(engine);
                return;
            }

            EnsureSelectionContext(engine);
            RefreshPanel(engine);
            DrawOverlay(engine);
        }

        private void EnsureSelectionContext(GameEngine engine)
        {
            if (_selectionOwner == Entity.Null || !engine.World.IsAlive(_selectionOwner))
            {
                _selectionOwner = ResolveExistingLocalPlayer(engine);
                _ownsSelectionOwner = false;
            }

            if (_selectionOwner == Entity.Null || !engine.World.IsAlive(_selectionOwner))
            {
                _selectionOwner = engine.World.Create(
                    new Name { Value = "SpatialBoundsViewer" },
                    new PlayerOwner { PlayerId = 1 },
                    default(CommandSourceDragState));
                _ownsSelectionOwner = true;
            }

            if (!engine.World.Has<CommandSourceDragState>(_selectionOwner))
            {
                engine.World.Add(_selectionOwner, default(CommandSourceDragState));
            }

            engine.ClientLocalSeatTestBindings.BindSoleSeat(GlobalContext, _selectionOwner);
            if (engine.World.TryGet(_selectionOwner, out PlayerOwner playerOwner) && playerOwner.PlayerId > 0)
            {
            }
        }

        private void DrawOverlay(GameEngine engine)
        {
            ScreenOverlayBuffer? overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            if (overlay == null)
            {
                return;
            }

            int x = 18;
            int y = 18;
            DrawSelectionFeedback(engine, overlay);
            overlay.AddRect(x, y, 760, 344, PanelFill, PanelBorder, stableId: 53100, dirtySerial: 1);
            overlay.AddText(x + 16, y + 18, "Spatial Bounds Showcase", 22, TitleColor, stableId: 53101, dirtySerial: 1);
            overlay.AddText(x + 16, y + 48, "Bounds are Core-owned reusable semantics. VisualTransform drives projection only.", 14, HintColor, stableId: 53102, dirtySerial: 1);
            overlay.AddText(x + 16, y + 70, "LMB click | drag marquee | Shift additive (QueueModifier) | Ctrl toggle (PrecisionModifier)", 14, AccentColor, stableId: 53103, dirtySerial: 1);
            overlay.AddText(x + 16, y + 92, "Orange outlines show footprint polygons directly, including disjoint multi-polygon shapes.", 14, HintColor, stableId: 53104, dirtySerial: 1);
            overlay.AddText(x + 16, y + 110, "No physics truth is implied here; downstream physics can sink these profiles when needed.", 14, HintColor, stableId: 53108, dirtySerial: 1);

            string hovered = ResolveEntityLabel(engine.World, ResolveHoveredEntity(engine)) ?? "(none)";
            string primary = ResolveEntityLabel(engine.World, ResolvePrimary(engine)) ?? "(none)";
            string selected = BuildSelectionPreview(engine);
            overlay.AddText(x + 16, y + 142, $"Hovered {hovered}", 15, TextColor, stableId: 53105, dirtySerial: 1);
            overlay.AddText(x + 16, y + 164, $"Primary {primary}", 15, TextColor, stableId: 53106, dirtySerial: 1);
            overlay.AddText(x + 16, y + 186, $"Selected {selected}", 14, TextColor, stableId: 53107, dirtySerial: 1);

            int lineY = y + 222;
            for (int i = 0; i < SpatialBoundsShowcaseIds.Descriptors.Length; i++)
            {
                ShowcaseEntityDescriptor descriptor = SpatialBoundsShowcaseIds.Descriptors[i];
                string entry = $"{descriptor.Name} [{descriptor.Kind}] {descriptor.Hint}";
                overlay.AddText(x + 16, lineY + (i * 18), entry, 13, TextColor, stableId: 53120 + i, dirtySerial: 1);
            }
        }

        private void DrawSelectionFeedback(GameEngine engine, ScreenOverlayBuffer overlay)
        {
            if (engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector)
            {
                return;
            }

            DrawFootprintPolygons(engine, overlay, projector);

            Entity hovered = ResolveHoveredEntity(engine);
            Entity primary = ResolvePrimary(engine);
            int selectedCount = GetCommandSourceCount(engine);
            Span<Entity> selected = selectedCount <= 16
                ? stackalloc Entity[Math.Max(selectedCount, 1)]
                : new Entity[selectedCount];
            int written = selectedCount > 0
                ? CopyCommandSource(engine, selected)
                : 0;

            for (int i = 0; i < written; i++)
            {
                Entity entity = selected[i];
                if (entity == primary)
                {
                    continue;
                }

                AddEntityHighlight(engine, overlay, projector, entity, SelectedFill, SelectedBorder, 54100 + i, includeLabel: false);
            }

            AddEntityHighlight(engine, overlay, projector, hovered, HoverFill, HoverBorder, 54001, includeLabel: true);
            AddEntityHighlight(engine, overlay, projector, primary, PrimaryFill, PrimaryBorder, 54002, includeLabel: true);
        }

        private static void DrawFootprintPolygons(GameEngine engine, ScreenOverlayBuffer overlay, IScreenProjector projector)
        {
            var polygonScratch = new Vector2[SpatialFootprint2D.MaxVerticesPerPolygon];
            int stableId = 55000;

            var query = new QueryDescription().WithAll<SpatialBounds, SpatialFootprint2D>();
            engine.World.Query(in query, (Entity entity, ref SpatialBounds bounds, ref SpatialFootprint2D footprint) =>
            {
                if (bounds.Kind != SpatialBoundsKind.Footprint2D)
                {
                    return;
                }

                for (int polygonIndex = 0; polygonIndex < footprint.PolygonCount; polygonIndex++)
                {
                    if (!SpatialBoundsUtility.TryProjectFootprintScreenPolygon(
                            engine.World,
                            entity,
                            projector,
                            polygonIndex,
                            polygonScratch,
                            out int count) ||
                        count < 3)
                    {
                        continue;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        Vector2 from = polygonScratch[i];
                        Vector2 to = polygonScratch[(i + 1) % count];
                        DrawSegment(overlay, from, to, FootprintStroke, stableId);
                        stableId += 8;

                        DrawPoint(overlay, from, FootprintVertex, stableId);
                        stableId += 8;
                    }
                }
            });
        }

        private static void AddEntityHighlight(
            GameEngine engine,
            ScreenOverlayBuffer overlay,
            IScreenProjector projector,
            Entity entity,
            Vector4 fill,
            Vector4 border,
            int stableIdBase,
            bool includeLabel)
        {
            if (entity == Entity.Null ||
                !engine.World.IsAlive(entity) ||
                !SpatialBoundsUtility.TryProjectScreenBounds(engine.World, entity, projector, out ScreenRect bounds))
            {
                return;
            }

            const int padding = 6;
            float width = bounds.MaxX - bounds.MinX;
            float height = bounds.MaxY - bounds.MinY;
            float centerX = (bounds.MinX + bounds.MaxX) * 0.5f;
            float centerY = (bounds.MinY + bounds.MaxY) * 0.5f;
            float renderWidth = MathF.Max(width, 16f) + (padding * 2);
            float renderHeight = MathF.Max(height, 16f) + (padding * 2);
            int x = (int)MathF.Round(centerX - (renderWidth * 0.5f));
            int y = (int)MathF.Round(centerY - (renderHeight * 0.5f));
            int rectWidth = (int)MathF.Round(renderWidth);
            int rectHeight = (int)MathF.Round(renderHeight);
            overlay.AddRect(x, y, rectWidth, rectHeight, fill, border, stableIdBase, dirtySerial: 1);

            if (!includeLabel)
            {
                return;
            }

            string label = ResolveEntityLabel(engine.World, entity) ?? $"Entity#{entity.Id}";
            overlay.AddText(x, Math.Max(4, y - 20), label, 14, border, stableIdBase + 1000, dirtySerial: 1);
        }

        private static void DrawSegment(ScreenOverlayBuffer overlay, Vector2 from, Vector2 to, Vector4 color, int stableIdBase)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float length = MathF.Sqrt((dx * dx) + (dy * dy));
            if (length <= 1f)
            {
                DrawPoint(overlay, from, color, stableIdBase);
                return;
            }

            int steps = Math.Max(1, (int)MathF.Ceiling(length / 8f));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var point = new Vector2(
                    from.X + (dx * t),
                    from.Y + (dy * t));
                DrawPoint(overlay, point, color, stableIdBase + i);
            }
        }

        private static void DrawPoint(ScreenOverlayBuffer overlay, Vector2 point, Vector4 color, int stableId)
        {
            int x = (int)MathF.Round(point.X) - 2;
            int y = (int)MathF.Round(point.Y) - 2;
            overlay.AddRect(x, y, 4, 4, color, color, stableId, dirtySerial: 1);
        }

        public bool TryResetCamera(GameEngine engine)
        {
            CameraConfig? cameraConfig = engine.CurrentMapSession?.MapConfig?.DefaultCamera;
            if (cameraConfig == null)
            {
                return false;
            }

            string virtualCameraId = string.IsNullOrWhiteSpace(cameraConfig.VirtualCameraId)
                ? "Camera.Profile.Tactical"
                : cameraConfig.VirtualCameraId;

            if (engine.GetService(CoreServiceKeys.VirtualCameraRegistry) is not VirtualCameraRegistry registry ||
                !registry.TryGet(virtualCameraId, out VirtualCameraDefinition? definition) ||
                definition == null)
            {
                return false;
            }

            engine.GameSession.Camera.ResetVirtualCameras();
            engine.GameSession.Camera.ActivateVirtualCamera(
                virtualCameraId,
                blendDurationSeconds: 0f,
                followTarget: CameraFollowTargetFactory.Build(
                    engine.World,
                    engine.GlobalContext,
                    definition.FollowTargetKind,
                    _selectionOwner,
                    definition.FollowCollectionKey),
                snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable);

            engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = virtualCameraId,
                TargetCm = (cameraConfig.TargetXCm.HasValue || cameraConfig.TargetYCm.HasValue)
                    ? new Vector2(cameraConfig.TargetXCm ?? 0f, cameraConfig.TargetYCm ?? 0f)
                    : null,
                Yaw = cameraConfig.Yaw,
                Pitch = cameraConfig.Pitch,
                DistanceCm = cameraConfig.DistanceCm,
                FovYDeg = cameraConfig.FovYDeg
            });

            RefreshPanel(engine);
            return true;
        }

        private void RefreshPanel(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.MountOrSync(root, engine, BuildPanelState(engine));
        }

        private static SpatialBoundsShowcasePanelState BuildPanelState(GameEngine engine)
        {
            Vector2 target = engine.GameSession.Camera.State.TargetCm;
            return new SpatialBoundsShowcasePanelState(
                "Spatial Bounds",
                $"Camera ({target.X:0},{target.Y:0})  Dist {engine.GameSession.Camera.State.DistanceCm:0}",
                "Use the button to recenter the showcase camera if you pan away.");
        }

        private string BuildSelectionPreview(GameEngine engine)
        {
            int count = GetCommandSourceCount(engine);
            if (count <= 0)
            {
                return "(empty)";
            }

            Span<Entity> selected = count <= 16
                ? stackalloc Entity[count]
                : new Entity[count];
            int written = CopyCommandSource(engine, selected);
            if (written <= 0)
            {
                return "(empty)";
            }

            var names = new List<string>(Math.Min(written, 5) + 1);
            int previewCount = Math.Min(written, 5);
            for (int i = 0; i < previewCount; i++)
            {
                names.Add(ResolveEntityLabel(engine.World, selected[i]) ?? $"Entity#{selected[i].Id}");
            }

            if (written > previewCount)
            {
                names.Add($"+{written - previewCount} more");
            }

            return string.Join(", ", names);
        }

        private static Entity ResolveExistingLocalPlayer(GameEngine engine)
        {
            return engine.ClientLocalSeatAccess.TryGetSolePossessedRep(GlobalContext, out Entity local) &&
                   engine.World.IsAlive(local)
                ? local
                : Entity.Null;
        }

        private static Entity ResolvePrimary(GameEngine engine)
        {
            Entity owner = ResolveExistingLocalPlayer(engine);
            return owner != Entity.Null &&
                   EntityCollectionContextRuntime.TryGetPrimary(
                       engine.World,
                       engine.GlobalContext,
                       owner,
                       EntityCollectionKeys.CommandSource,
                       out Entity primary)
                ? primary
                : Entity.Null;
        }

        private static int GetCommandSourceCount(GameEngine engine)
        {
            Entity owner = ResolveExistingLocalPlayer(engine);
            return owner != Entity.Null
                ? EntityCollectionContextRuntime.GetCount(engine.GlobalContext, owner, EntityCollectionKeys.CommandSource)
                : 0;
        }

        private static int CopyCommandSource(GameEngine engine, Span<Entity> destination)
        {
            Entity owner = ResolveExistingLocalPlayer(engine);
            return owner != Entity.Null
                ? EntityCollectionContextRuntime.Copy(engine.GlobalContext, owner, EntityCollectionKeys.CommandSource, destination)
                : 0;
        }

        private static Entity ResolveHoveredEntity(GameEngine engine)
        {
            Entity owner = ResolveExistingLocalPlayer(engine);
            return owner != Entity.Null &&
                   EntityCollectionContextRuntime.TryGetPrimary(
                       engine.World,
                       engine.GlobalContext,
                       owner,
                       EntityCollectionKeys.HoveredEntity,
                       out Entity hovered) &&
                   hovered != Entity.Null &&
                   engine.World.IsAlive(hovered)
                ? hovered
                : Entity.Null;
        }

        private static string? ResolveEntityLabel(World world, Entity entity)
        {
            if (entity == Entity.Null || !world.IsAlive(entity))
            {
                return null;
            }

            return world.TryGet(entity, out Name name) ? name.Value : null;
        }

        private void Disable(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _panelController.ClearIfOwned(root);
            }

            if (_ownsSelectionOwner && engine.World.IsAlive(_selectionOwner))
            {
                engine.World.Destroy(_selectionOwner);
            }

            if (engine.ClientLocalSeatAccess.TryGetSolePossessedRep(GlobalContext, out Entity local) &&
                local == _selectionOwner)
            {
                ClientLocalSeatAccess.RequireRegistry(engine).Clear();
            }

            _selectionOwner = Entity.Null;
            _ownsSelectionOwner = false;
        }
    }
}
