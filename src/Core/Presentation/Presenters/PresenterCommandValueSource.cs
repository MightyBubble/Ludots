namespace Ludots.Core.Presentation.Presenters
{
    public enum PresenterCommandValueSource : byte
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
        EventPositionX = 9,
        EventPositionY = 10,
        EventPositionZ = 11,
    }
}
