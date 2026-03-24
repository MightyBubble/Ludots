namespace Ludots.Core.Gameplay.Relationships
{
    public readonly struct RelationshipMetricDefinition
    {
        public RelationshipMetricDefinition(int id, string name, short minValue, short maxValue, short defaultValue)
        {
            Id = id;
            Name = name ?? string.Empty;
            MinValue = minValue;
            MaxValue = maxValue;
            DefaultValue = defaultValue;
        }

        public int Id { get; }
        public string Name { get; }
        public short MinValue { get; }
        public short MaxValue { get; }
        public short DefaultValue { get; }
    }
}
