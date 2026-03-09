using System;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Ludots.Core.Input.Selection
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SelectionPreviewShapeKind : byte
    {
        None = 0,
        Rectangle = 1,
        Polygon = 2,
    }

    public sealed class SelectionInteractionState
    {
        public bool IsPointerDown { get; set; }
        public bool IsDragging { get; set; }
        public Vector2 AnchorScreen { get; set; }
        public Vector2 CurrentScreen { get; set; }
        public Vector2 LastPointerScreen { get; set; }
        public SelectionPreviewShapeKind PreviewShape { get; private set; }
        public Vector2[] PolygonScreen { get; private set; } = Array.Empty<Vector2>();

        public void BeginPointer(Vector2 anchorScreen)
        {
            ClearPreview();
            IsPointerDown = true;
            AnchorScreen = anchorScreen;
            CurrentScreen = anchorScreen;
            LastPointerScreen = anchorScreen;
        }

        public void SetRectangle(Vector2 anchorScreen, Vector2 currentScreen)
        {
            AnchorScreen = anchorScreen;
            CurrentScreen = currentScreen;
            PreviewShape = SelectionPreviewShapeKind.Rectangle;
            PolygonScreen = Array.Empty<Vector2>();
            IsDragging = true;
        }

        public void SetPolygon(Vector2[] polygonScreen)
        {
            PolygonScreen = polygonScreen ?? Array.Empty<Vector2>();
            PreviewShape = PolygonScreen.Length >= 2
                ? SelectionPreviewShapeKind.Polygon
                : SelectionPreviewShapeKind.None;
            IsDragging = PreviewShape != SelectionPreviewShapeKind.None;
        }

        public void ClearPreview()
        {
            IsPointerDown = false;
            IsDragging = false;
            PreviewShape = SelectionPreviewShapeKind.None;
            PolygonScreen = Array.Empty<Vector2>();
        }
    }
}
