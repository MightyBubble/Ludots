namespace Ludots.Core.Presentation.Presenters
{
    public enum PresenterCommandScopeSource : byte
    {
        Fixed = 0,
        EventPayloadA = 1,
        EventPayloadB = 2,
        EventKeyId = 3,
        SourceStableId = 4,
        EventTargetStableId = 5,
    }
}
