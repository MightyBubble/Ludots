using Arch.Core;
using System.Numerics;

namespace Ludots.Core.Presentation.Events
{
    public struct PresentationEvent
    {
        public int LogicTickStamp;
        public PresentationEventKind Kind;
        public int KeyId;
        public Entity Source;
        public Entity Target;
        public Entity Viewer;
        public float Magnitude;
        public int PayloadA;
        public int PayloadB;
        public float FloatA;
        public float FloatB;
        public float FloatC;
        public float FloatD;
        public Vector3 Position;
        public Entity PresenterEntity;
    }
}
