namespace Ludots.Core.Input.Selection
{
    public sealed class PolygonSelectionDragGesture : ISelectionDragGesture
    {
        public SelectionPreviewShapeKind Shape => SelectionPreviewShapeKind.Polygon;

        public void UpdatePreview(
            in SelectionInputFrame frame,
            SelectionProfile profile,
            SelectionPointerSessionState session,
            SelectionInteractionState interaction)
        {
            session.RecordDragPoint(frame.PointerScreen, profile.PolygonPointSpacingPx);
            interaction.SetPolygon(session.SnapshotPolygon(frame.PointerScreen, profile.PolygonPointSpacingPx));
        }

        public bool TryCreateCommand(
            in SelectionInputFrame frame,
            SelectionProfile profile,
            SelectionPointerSessionState session,
            SelectionInteractionState interaction,
            out SelectionInputCommand command)
        {
            var polygon = interaction.PolygonScreen;
            if (polygon == null || polygon.Length < 3)
            {
                command = default;
                return false;
            }

            command = SelectionInputCommand.CreatePolygon(polygon, frame.ApplyMode);
            return true;
        }
    }
}
