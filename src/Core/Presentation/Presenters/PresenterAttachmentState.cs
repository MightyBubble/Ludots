using System.Numerics;

namespace Ludots.Core.Presentation.Presenters
{
    public struct PresenterAttachmentState
    {
        public bool Initialized;
        public bool ParametersValid;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
        public Vector3 PositionOrigin;
        public Quaternion PositionRotation;
        public Vector3 PositionScale;
    }
}
