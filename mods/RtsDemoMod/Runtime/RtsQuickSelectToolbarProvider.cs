using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace RtsDemoMod.Runtime
{
    internal sealed class RtsQuickSelectToolbarProvider : IEntityCommandPanelToolbarProvider
    {
        private static readonly ToolbarButtonSpec[] Buttons =
        {
            new(ToolbarButtonKind.SelectEntity, "war3_build", "W3 Build", "Peasant", "#93C572"),
            new(ToolbarButtonKind.SelectEntity, "war3_train", "W3 Train", "Barracks", "#D4A15A"),
            new(ToolbarButtonKind.SelectEntity, "war3_garrison", "W3 Tower", "Footman", "#C8A96B"),
            new(ToolbarButtonKind.SelectEntity, "cnc_build", "C&C Build", "Construction Yard", "#F18F5A"),
            new(ToolbarButtonKind.SelectEntity, "cnc_train", "C&C Train", "War Factory", "#B889FF"),
            new(ToolbarButtonKind.SelectEntity, "cnc_garrison", "C&C Bunker", "Rocket Trooper", "#FF8FA3"),
            new(ToolbarButtonKind.SelectEntity, "sc2_train", "SC2 Train", "Gateway", "#62C8F3"),
            new(ToolbarButtonKind.SelectEntity, "zerg_morph", "Zerg Morph", "Drone", "#7ED957"),
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

        public string Title => ResolveToolbarProfile().Title;

        public string Subtitle => ResolveToolbarProfile().Subtitle;

        public int CopyButtons(Span<EntityCommandPanelToolbarButtonView> destination)
        {
            if (!IsVisible || destination.IsEmpty)
            {
                return 0;
            }

            string selectedName = ResolveCurrentPrimaryName();
            ToolbarProfile profile = ResolveToolbarProfile();
            int written = 0;
            for (int i = 0; i < Buttons.Length && written < destination.Length; i++)
            {
                ref readonly ToolbarButtonSpec button = ref Buttons[i];
                if (!profile.Accepts(button.ButtonId))
                {
                    continue;
                }

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
            RtsShowcaseSelectionHelper.TrySelectAndFocus(_engine, target, snapCamera: true);
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

        private ToolbarProfile ResolveToolbarProfile()
        {
            ScenarioKind scenario = ResolveScenarioKind();
            return scenario switch
            {
                ScenarioKind.War3Training => new ToolbarProfile(
                    "War3 Training Yard",
                    "Goal: queue Footman. Watch for upfront cost, Barracks progress, and the finished unit stepping out of the building.",
                    "war3_train",
                    "camera_reset"),
                ScenarioKind.CncTraining => new ToolbarProfile(
                    "C&C Factory Line",
                    "Goal: queue Rhino. Watch for staged credit drain pulses, factory progress, and the tank rolling out when the bar completes.",
                    "cnc_train",
                    "camera_reset"),
                ScenarioKind.Sc2Training => new ToolbarProfile(
                    "SC2 Gateway Drill",
                    "Goal: queue Zealot. Watch for upfront cost, gateway charge-up, and the unit materializing when training completes.",
                    "sc2_train",
                    "camera_reset"),
                _ => new ToolbarProfile(
                    "RTS Sandbox",
                    "Select a showcase unit, try one focused command, and use Reset Cam if the board drifts out of view.")
            };
        }

        private ScenarioKind ResolveScenarioKind()
        {
            var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
            if (tags == null)
            {
                return ScenarioKind.None;
            }

            bool isTraining = false;
            bool war3 = false;
            bool cnc = false;
            bool sc2 = false;

            for (int i = 0; i < tags.Count; i++)
            {
                string tag = tags[i];
                if (string.Equals(tag, "rts_training", StringComparison.OrdinalIgnoreCase))
                {
                    isTraining = true;
                }
                else if (string.Equals(tag, "war3", StringComparison.OrdinalIgnoreCase))
                {
                    war3 = true;
                }
                else if (string.Equals(tag, "cnc", StringComparison.OrdinalIgnoreCase))
                {
                    cnc = true;
                }
                else if (string.Equals(tag, "sc2", StringComparison.OrdinalIgnoreCase))
                {
                    sc2 = true;
                }
            }

            if (!isTraining)
            {
                return ScenarioKind.None;
            }

            if (war3)
            {
                return ScenarioKind.War3Training;
            }

            if (cnc)
            {
                return ScenarioKind.CncTraining;
            }

            if (sc2)
            {
                return ScenarioKind.Sc2Training;
            }

            return ScenarioKind.None;
        }

        private enum ToolbarButtonKind : byte
        {
            SelectEntity,
            ResetCamera
        }

        private enum ScenarioKind : byte
        {
            None,
            War3Training,
            CncTraining,
            Sc2Training
        }

        private readonly record struct ToolbarButtonSpec(
            ToolbarButtonKind Kind,
            string ButtonId,
            string Label,
            string EntityName,
            string AccentColorHex);

        private readonly record struct ToolbarProfile(
            string Title,
            string Subtitle,
            string PrimaryButtonId = "",
            string SecondaryButtonId = "")
        {
            public bool Accepts(string buttonId)
            {
                if (string.IsNullOrWhiteSpace(PrimaryButtonId) && string.IsNullOrWhiteSpace(SecondaryButtonId))
                {
                    return true;
                }

                return string.Equals(buttonId, PrimaryButtonId, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(buttonId, SecondaryButtonId, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
