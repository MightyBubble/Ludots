using System.Numerics;

namespace Ludots.Core.Physics3D;

public struct Physics3DBodyCm
{
    public Physics3DBodyId Id;
    public Physics3DBodyKind Kind;
}

public struct Physics3DPoseCm
{
    public Vector3 Position;
    public Quaternion Orientation;
    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;
}

public struct PreviousPhysics3DPoseCm
{
    public Vector3 Position;
    public Quaternion Orientation;
}
