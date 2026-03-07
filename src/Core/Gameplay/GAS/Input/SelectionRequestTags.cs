namespace Ludots.Core.Gameplay.GAS.Input
{
    /// <summary>
    /// Built-in selection request type ids for the generic selection-response pipeline.
    /// These ids are not gameplay tags; they key selection rules and optional presentation hooks.
    /// </summary>
    public static class SelectionRequestTags
    {
        public const int DefaultAreaAll = 0;
        public const int Single = 1;
        public const int CircleEnemy = 2;
        public const int CircleAll = 3;
    }
}
