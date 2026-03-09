namespace Ludots.Core.Input.Selection
{
    public interface ISelectionDragGesture
    {
        SelectionPreviewShapeKind Shape { get; }

        void UpdatePreview(
            in SelectionInputFrame frame,
            SelectionProfile profile,
            SelectionPointerSessionState session,
            SelectionInteractionState interaction);

        bool TryCreateCommand(
            in SelectionInputFrame frame,
            SelectionProfile profile,
            SelectionPointerSessionState session,
            SelectionInteractionState interaction,
            out SelectionInputCommand command);
    }
}
