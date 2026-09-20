namespace Ludots.Core.Gameplay.GAS.LiveSkillWorkbench
{
    /// <summary>
    /// Entry channel for a live skill workbench edit session.
    /// All sources stage into the same debug patch model.
    /// </summary>
    public enum LiveEditSource : byte
    {
        ManualWorkbench = 1,
        FileChange = 2,
        AiGeneratedDraft = 3
    }
}
