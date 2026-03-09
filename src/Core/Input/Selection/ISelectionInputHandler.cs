namespace Ludots.Core.Input.Selection
{
    /// <summary>
    /// Shared selection-input contract.
    /// Implementations translate raw device state into high-level selection commands.
    /// </summary>
    public interface ISelectionInputHandler
    {
        void Update(float dt);
        bool Poll(out SelectionInputCommand command);
    }
}
