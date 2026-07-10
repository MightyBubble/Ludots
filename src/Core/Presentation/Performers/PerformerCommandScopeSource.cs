namespace Ludots.Core.Presentation.Performers
{
    public enum PerformerCommandScopeSource : byte
    {
        Fixed = 0,
        EventPayloadA = 1,
        EventPayloadB = 2,
        EventKeyId = 3,
        SourceStableId = 4,
        EventTargetStableId = 5,
    }
}
