using Arch.Core;

namespace Ludots.Core.Presentation.Events
{
    public struct PresentationEvent
    {
        public int LogicTickStamp;
        public PresentationEventKind Kind;
        public int KeyId;
        public Entity Source;
        public Entity Target;
        public float Magnitude;
        public int PayloadA;
        public int PayloadB;
    }
}
