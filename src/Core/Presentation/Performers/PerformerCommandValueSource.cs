namespace Ludots.Core.Presentation.Performers
{
    public enum PerformerCommandValueSource : byte
    {
        Fixed = 0,
        EventKeyId = 1,
        EventPayloadA = 2,
        EventPayloadB = 3,
        EventMagnitude = 4,
        EventFloatA = 5,
        EventFloatB = 6,
        EventFloatC = 7,
        EventFloatD = 8,
    }
}
