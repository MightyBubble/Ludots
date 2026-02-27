namespace Ludots.Core.Diagnostics
{
    public readonly struct LogChannel
    {
        public readonly int Id;
        public readonly string Name;

        internal LogChannel(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }
}
