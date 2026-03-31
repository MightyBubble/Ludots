namespace Ludots.Core.Engine.TimeFlow
{
    public readonly struct TimeFlowToken
    {
        public TimeFlowToken(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value > 0;

        public static TimeFlowToken Invalid => default;
    }
}
