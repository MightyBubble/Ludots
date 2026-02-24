using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Presentation.Commands
{
    public struct PresentationCommand
    {
        public int LogicTickStamp;
        public PresentationCommandKind Kind;

        public int IdA;
        public int IdB;

        public Entity Source;
        public Entity Target;

        public Vector3 Position;
        public Vector4 Param0;
        public float Param1;
        public float Param2;
    }
}
