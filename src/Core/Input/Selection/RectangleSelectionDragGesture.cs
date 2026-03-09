using System.Numerics;

namespace Ludots.Core.Input.Selection
{
    public sealed class RectangleSelectionDragGesture : ISelectionDragGesture
    {
        public SelectionPreviewShapeKind Shape => SelectionPreviewShapeKind.Rectangle;

        public void UpdatePreview(
            in SelectionInputFrame frame,
            SelectionProfile profile,
            SelectionPointerSessionState session,
            SelectionInteractionState interaction)
        {
            interaction.SetRectangle(session.PressScreen, frame.PointerScreen);
        }

        public bool TryCreateCommand(
            in SelectionInputFrame frame,
            SelectionProfile profile,
            SelectionPointerSessionState session,
            SelectionInteractionState interaction,
            out SelectionInputCommand command)
        {
            Vector2 min = Vector2.Min(session.PressScreen, frame.PointerScreen);
            Vector2 max = Vector2.Max(session.PressScreen, frame.PointerScreen);
            command = SelectionInputCommand.CreateRectangle(min, max, frame.ApplyMode);
            return true;
        }
    }
}
