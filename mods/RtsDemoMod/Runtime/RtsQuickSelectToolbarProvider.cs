using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace RtsDemoMod.Runtime
{
    internal sealed class RtsQuickSelectToolbarProvider : IEntityCommandPanelToolbarProvider
    {
        private static readonly ToolbarButtonSpec[] Buttons =
        {
            new(ToolbarButtonKind.SelectEntity, "peasant", "Peasant", "Peasant", "#93C572"),
            new(ToolbarButtonKind.SelectEntity, "barracks", "Barracks", "Barracks", "#D4A15A"),
            new(ToolbarButtonKind.SelectEntity, "conyard", "ConYard", "Construction Yard", "#F18F5A"),
            new(ToolbarButtonKind.SelectEntity, "warfactory", "Factory", "War Factory", "#B889FF"),
            new(ToolbarButtonKind.SelectEntity, "gateway", "Gateway", "Gateway", "#62C8F3"),
            new(ToolbarButtonKind.SelectEntity, "drone", "Drone", "Drone", "#F07C9A"),
            new(ToolbarButtonKind.ResetCamera, "camera_reset", "Reset Cam", string.Empty, "#F2C36B")
        };

        private readonly GameEngine _engine;

        public RtsQuickSelectToolbarProvider(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public bool IsVisible => IsRtsMapActive();

        public uint Revision
        {
            get
            {
                if (!IsVisible)
                {
                    return 0;
                }

                string selectedName = ResolveCurrentPrimaryName();
                return string.IsNullOrWhiteSpace(selectedName)
                    ? 1u
                    : unchecked((uint)selectedName.GetHashCode(StringComparison.OrdinalIgnoreCase));
            }
        }

        public string Title => "RTS Quick Select";

        public string Subtitle => "Click a unit/building, or tap Reset Cam to snap the RTS view back to the map default.";

        public int CopyButtons(Span<EntityCommandPanelToolbarButtonView> destination)
        {
            if (!IsVisible || destination.IsEmpty)
            {
                return 0;
            }

            string selectedName = ResolveCurrentPrimaryName();
            int written = 0;
            for (int i = 0; i < Buttons.Length && written < destination.Length; i++)
            {
                ref readonly ToolbarButtonSpec button = ref Buttons[i];
                if (button.Kind == ToolbarButtonKind.SelectEntity && !TryFindEntity(button.EntityName, out _))
                {
                    continue;
                }

                bool active = button.Kind == ToolbarButtonKind.SelectEntity &&
                              string.Equals(selectedName, button.EntityName, StringComparison.OrdinalIgnoreCase);
                destination[written++] = new EntityCommandPanelToolbarButtonView(
                    button.ButtonId,
                    button.Label,
                    active,
                    button.AccentColorHex);
            }

            return written;
        }

        public void Activate(string buttonId)
        {
            if (!IsVisible || string.IsNullOrWhiteSpace(buttonId))
            {
                return;
            }

            for (int i = 0; i < Buttons.Length; i++)
            {
                ref readonly ToolbarButtonSpec button = ref Buttons[i];
                if (!string.Equals(button.ButtonId, buttonId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (button.Kind == ToolbarButtonKind.ResetCamera)
                {
                    ResetCameraToMapDefault();
                    return;
                }

                if (TryFindEntity(button.EntityName, out Entity target))
                {
                    SelectEntity(target);
                }

                return;
            }
        }

        private string ResolveCurrentPrimaryName()
        {
            if (!SelectionContextRuntime.TryGetCurrentPrimary(_engine.World, _engine.GlobalContext, out Entity primary) ||
                !_engine.World.IsAlive(primary) ||
                !_engine.World.Has<Name>(primary))
            {
                return string.Empty;
            }

            return _engine.World.Get<Name>(primary).Value ?? string.Empty;
        }

        private void SelectEntity(Entity target)
        {
            SelectionRuntime? selection = _engine.GetService(CoreServiceKeys.SelectionRuntime);
            if (selection == null || !_engine.World.IsAlive(target))
            {
                return;
            }

            Entity owner = ResolveSelectionOwner();
            if (!_engine.World.IsAlive(owner))
            {
                return;
            }

            Span<Entity> next = stackalloc Entity[1];
            next[0] = target;
            selection.ReplaceSelection(owner, SelectionSetKeys.Ambient, next);
            selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.Ambient);
            _engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
            _engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
        }

        private Entity ResolveSelectionOwner()
        {
            Entity owner = _engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (_engine.World.IsAlive(owner))
            {
                return owner;
            }

            owner = _engine.World.Create(new PlayerOwner { PlayerId = 1 });
            _engine.SetService(CoreServiceKeys.LocalPlayerEntity, owner);
            return owner;
        }

        private bool TryFindEntity(string entityName, out Entity result)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            _engine.World.Query(in query, (Entity entity, ref Name name) =>
            {
                if (found != Entity.Null ||
                    string.IsNullOrWhiteSpace(name.Value))
                {
                    return;
                }

                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase) ||
                    name.Value.IndexOf(entityName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = entity;
                }
            });

            result = found;
            return found != Entity.Null;
        }

        private void ResetCameraToMapDefault()
        {
            MapConfig? mapConfig = _engine.CurrentMapSession?.MapConfig;
            if (mapConfig == null)
            {
                return;
            }

            CameraConfig? cam = mapConfig.DefaultCamera;
            string virtualCameraId = string.IsNullOrWhiteSpace(cam?.VirtualCameraId)
                ? "Default"
                : cam.VirtualCameraId;

            _engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
            {
                Id = virtualCameraId,
                BlendDurationSeconds = 0f,
                SnapToFollowTargetWhenAvailable = true,
                ResetRuntimeState = true
            };

            _engine.GlobalContext[CoreServiceKeys.CameraPoseRequest.Name] = new CameraPoseRequest
            {
                VirtualCameraId = virtualCameraId,
                TargetCm = (cam?.TargetXCm.HasValue == true || cam?.TargetYCm.HasValue == true)
                    ? new System.Numerics.Vector2(cam?.TargetXCm ?? 0f, cam?.TargetYCm ?? 0f)
                    : null,
                Yaw = cam?.Yaw,
                Pitch = cam?.Pitch,
                DistanceCm = cam?.DistanceCm,
                FovYDeg = cam?.FovYDeg
            };
        }

        private bool IsRtsMapActive()
        {
            var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
            if (tags == null)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], "rts", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tags[i], "rts_showcase", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private enum ToolbarButtonKind : byte
        {
            SelectEntity,
            ResetCamera
        }

        private readonly record struct ToolbarButtonSpec(
            ToolbarButtonKind Kind,
            string ButtonId,
            string Label,
            string EntityName,
            string AccentColorHex);
    }
}
