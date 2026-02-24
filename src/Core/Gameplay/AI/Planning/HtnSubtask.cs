namespace Ludots.Core.Gameplay.AI.Planning
{
    public enum HtnSubtaskKind : byte
    {
        Action = 0,
        Compound = 1
    }

    public readonly struct HtnSubtask
    {
        public readonly HtnSubtaskKind Kind;
        public readonly int Id;

        public HtnSubtask(HtnSubtaskKind kind, int id)
        {
            Kind = kind;
            Id = id;
        }

        public static HtnSubtask Action(int actionOpId) => new HtnSubtask(HtnSubtaskKind.Action, actionOpId);
        public static HtnSubtask Compound(int taskId) => new HtnSubtask(HtnSubtaskKind.Compound, taskId);
    }
}

