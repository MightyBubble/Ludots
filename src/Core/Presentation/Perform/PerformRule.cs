using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Perform
{
    /// <summary>
    /// Declarative trigger rule for perform orchestration.
    /// First-wave implementation keeps the existing event and condition infrastructure.
    /// </summary>
    public struct PerformRule
    {
        public EventFilter Event;
        public ConditionRef Condition;
        public PerformCommand Command;

        public static PerformRule FromLegacy(in PerformerRule rule)
        {
            return new PerformRule
            {
                Event = rule.Event,
                Condition = rule.Condition,
                Command = PerformCommand.FromLegacy(rule.Command),
            };
        }

        public readonly PerformerRule ToLegacy()
        {
            return new PerformerRule
            {
                Event = Event,
                Condition = Condition,
                Command = Command.ToLegacy(),
            };
        }
    }
}
