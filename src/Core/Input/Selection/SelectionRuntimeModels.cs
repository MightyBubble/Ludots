using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Selection
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SelectionPreviewShapeKind : byte
    {
        Rectangle = 0,
        Polygon = 1,
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SelectionApplyMode : byte
    {
        Replace = 0,
        Add = 1,
        Toggle = 2,
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SelectionCommandKind : byte
    {
        SelectAtPoint = 0,
        SelectInRectangle = 1,
        SelectInPolygon = 2,
        Clear = 3,
        SaveGroup = 4,
        RecallGroup = 5,
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SelectionPointExpansionKind : byte
    {
        None = 0,
        SameClassDoubleClick = 1,
    }

    public sealed class SelectionProfile : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public float DragThresholdPx { get; set; } = 8f;
        public float PickRadiusPx { get; set; } = 12f;
        public float DoubleClickWindowSec { get; set; } = 0.3f;
        public float DoubleClickMaxDistancePx { get; set; } = 16f;
        public SelectionPreviewShapeKind DragSelectionShape { get; set; } = SelectionPreviewShapeKind.Rectangle;
        public float PolygonPointSpacingPx { get; set; } = 12f;
        public bool EnableAdditiveSelection { get; set; } = true;
        public bool EnableToggleSelection { get; set; } = true;
        public SelectionPointExpansionKind PointExpansion { get; set; } = SelectionPointExpansionKind.None;
        public string AddModifierActionId { get; set; } = "QueueModifier";
        public string ToggleModifierActionId { get; set; } = "PrecisionModifier";
        public string GroupAssignModifierActionId { get; set; } = "PrecisionModifier";
        public string[] GroupHotkeyActionIds { get; set; } = Array.Empty<string>();
    }

    public sealed class SelectionProfileRegistry : DataRegistry<SelectionProfile>
    {
        public SelectionProfileRegistry(ConfigPipeline pipeline) : base(pipeline)
        {
        }
    }

    public sealed class SelectionInteractionState
    {
        public bool IsDragging { get; private set; }
        public SelectionPreviewShapeKind PreviewShape { get; private set; } = SelectionPreviewShapeKind.Rectangle;
        public Vector2 AnchorScreen { get; private set; }
        public Vector2 CurrentScreen { get; private set; }
        public Vector2[] PolygonScreen { get; private set; } = Array.Empty<Vector2>();

        public void Begin(Vector2 anchorScreen, SelectionPreviewShapeKind previewShape)
        {
            IsDragging = false;
            PreviewShape = previewShape;
            AnchorScreen = anchorScreen;
            CurrentScreen = anchorScreen;
            PolygonScreen = Array.Empty<Vector2>();
        }

        public void UpdatePointer(Vector2 currentScreen)
        {
            CurrentScreen = currentScreen;
        }

        public void StartDrag(Vector2 currentScreen, SelectionPreviewShapeKind previewShape)
        {
            IsDragging = true;
            PreviewShape = previewShape;
            CurrentScreen = currentScreen;
            PolygonScreen = previewShape == SelectionPreviewShapeKind.Polygon
                ? new[] { AnchorScreen, currentScreen }
                : Array.Empty<Vector2>();
        }

        public void AppendPolygonPoint(Vector2 point)
        {
            if (PreviewShape != SelectionPreviewShapeKind.Polygon)
            {
                return;
            }

            var polygon = PolygonScreen;
            int count = polygon.Length;
            Array.Resize(ref polygon, count + 1);
            polygon[count] = point;
            PolygonScreen = polygon;
        }

        public void Reset()
        {
            IsDragging = false;
            AnchorScreen = Vector2.Zero;
            CurrentScreen = Vector2.Zero;
            PolygonScreen = Array.Empty<Vector2>();
        }
    }

    public readonly struct SelectionInputCommand
    {
        public SelectionCommandKind Kind { get; init; }
        public SelectionApplyMode ApplyMode { get; init; }
        public Vector2 PointScreen { get; init; }
        public float PickRadiusPx { get; init; }
        public Vector2 RectangleMinScreen { get; init; }
        public Vector2 RectangleMaxScreen { get; init; }
        public Vector2[]? PolygonScreen { get; init; }
        public bool ExpandSameClassFromResolvedCandidate { get; init; }
        public int GroupIndex { get; init; }

        public static SelectionInputCommand CreatePoint(Vector2 pointScreen, float pickRadiusPx, SelectionApplyMode applyMode, bool expandSameClass = false)
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.SelectAtPoint,
                ApplyMode = applyMode,
                PointScreen = pointScreen,
                PickRadiusPx = pickRadiusPx,
                ExpandSameClassFromResolvedCandidate = expandSameClass,
            };
        }

        public static SelectionInputCommand CreateRectangle(Vector2 minScreen, Vector2 maxScreen, SelectionApplyMode applyMode)
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.SelectInRectangle,
                ApplyMode = applyMode,
                RectangleMinScreen = minScreen,
                RectangleMaxScreen = maxScreen,
            };
        }

        public static SelectionInputCommand CreatePolygon(Vector2[] polygonScreen, SelectionApplyMode applyMode)
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.SelectInPolygon,
                ApplyMode = applyMode,
                PolygonScreen = polygonScreen == null || polygonScreen.Length == 0 ? Array.Empty<Vector2>() : (Vector2[])polygonScreen.Clone(),
            };
        }

        public static SelectionInputCommand CreateClear()
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.Clear,
                ApplyMode = SelectionApplyMode.Replace,
            };
        }

        public static SelectionInputCommand CreateSaveGroup(int groupIndex)
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.SaveGroup,
                GroupIndex = groupIndex,
            };
        }

        public static SelectionInputCommand CreateRecallGroup(int groupIndex)
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.RecallGroup,
                GroupIndex = groupIndex,
            };
        }
    }

    public interface ISelectionCandidatePolicy
    {
        bool IsSelectable(World world, Entity controller, Entity candidate);
        bool IsSameSelectionClass(World world, Entity reference, Entity candidate);
    }

    public sealed class DefaultSelectionCandidatePolicy : ISelectionCandidatePolicy
    {
        public bool IsSelectable(World world, Entity controller, Entity candidate)
        {
            return world.IsAlive(candidate) && candidate != controller;
        }

        public bool IsSameSelectionClass(World world, Entity reference, Entity candidate)
        {
            return reference == candidate;
        }
    }

    public static class SelectionRuntime
    {
        public static bool TryGetController(World world, Dictionary<string, object> globals, out Entity controller)
        {
            if (globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var obj) && obj is Entity entity && world.IsAlive(entity))
            {
                controller = entity;
                return true;
            }

            controller = default;
            return false;
        }

        public static int ApplySelection(World world, Dictionary<string, object> globals, Entity controller, IReadOnlyList<Entity> entities, SelectionApplyMode applyMode)
        {
            if (!world.IsAlive(controller) || entities == null)
            {
                return 0;
            }

            if (!world.Has<SelectionBuffer>(controller))
            {
                world.Add(controller, new SelectionBuffer());
            }

            var selection = world.Get<SelectionBuffer>(controller);
            if (applyMode == SelectionApplyMode.Replace)
            {
                selection.Clear();
            }

            int applied = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (!world.IsAlive(entity))
                {
                    continue;
                }

                switch (applyMode)
                {
                    case SelectionApplyMode.Replace:
                    case SelectionApplyMode.Add:
                        if (selection.Add(entity))
                        {
                            applied++;
                        }
                        break;

                    case SelectionApplyMode.Toggle:
                        if (selection.Contains(entity))
                        {
                            if (selection.Remove(entity))
                            {
                                applied++;
                            }
                        }
                        else if (selection.Add(entity))
                        {
                            applied++;
                        }
                        break;
                }
            }

            world.Set(controller, selection);
            globals[CoreServiceKeys.SelectedEntity.Name] = selection.Count > 0 ? selection.Primary : controller;
            return applied;
        }

        public static int CollectSelected(World world, Dictionary<string, object> globals, List<Entity> selected)
        {
            if (selected == null)
            {
                throw new ArgumentNullException(nameof(selected));
            }

            selected.Clear();
            if (!globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var obj) || obj is not Entity controller || !world.IsAlive(controller) || !world.Has<SelectionBuffer>(controller))
            {
                return 0;
            }

            var selection = world.Get<SelectionBuffer>(controller);
            for (int i = 0; i < selection.Count; i++)
            {
                var entity = selection.Get(i);
                if (world.IsAlive(entity))
                {
                    selected.Add(entity);
                }
            }

            return selected.Count;
        }
    }
}
