using System;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Selection
{
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
        public float PickRadiusPx { get; set; } = 28f;
        public float DoubleClickWindowSec { get; set; } = 0.30f;
        public float DoubleClickMaxDistancePx { get; set; } = 12f;
        public SelectionPreviewShapeKind DragSelectionShape { get; set; } = SelectionPreviewShapeKind.Rectangle;
        public float PolygonPointSpacingPx { get; set; } = 18f;
        public bool EnableAdditiveSelection { get; set; } = true;
        public bool EnableToggleSelection { get; set; } = true;
        public SelectionPointExpansionKind PointExpansion { get; set; } = SelectionPointExpansionKind.SameClassDoubleClick;
        public string AddModifierActionId { get; set; } = "QueueModifier";
        public string ToggleModifierActionId { get; set; } = "PrecisionModifier";
        public string GroupAssignModifierActionId { get; set; } = "PrecisionModifier";
        public string[] GroupHotkeyActionIds { get; set; } = Array.Empty<string>();
    }
}
