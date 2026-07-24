using System;
using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Physics3D;

public sealed class Physics3DAwakeBodyBuffer
{
    private readonly Physics3DBodyId[] _bodyIds;
    private readonly Entity[] _entities;
    private readonly Vector3[] _positionsCm;
    private readonly Quaternion[] _orientations;
    private readonly Vector3[] _linearVelocitiesCmPerSecond;
    private readonly Vector3[] _angularVelocitiesRadiansPerSecond;
    private readonly Physics3DBodyKind[] _bodyKinds;

    public Physics3DAwakeBodyBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _bodyIds = new Physics3DBodyId[capacity];
        _entities = new Entity[capacity];
        _positionsCm = new Vector3[capacity];
        _orientations = new Quaternion[capacity];
        _linearVelocitiesCmPerSecond = new Vector3[capacity];
        _angularVelocitiesRadiansPerSecond = new Vector3[capacity];
        _bodyKinds = new Physics3DBodyKind[capacity];
    }

    public int Capacity => _bodyIds.Length;
    public int Count { get; private set; }
    public long StepIndex { get; private set; }
    public ReadOnlySpan<Physics3DBodyId> BodyIds => _bodyIds.AsSpan(0, Count);
    public ReadOnlySpan<Entity> Entities => _entities.AsSpan(0, Count);
    public ReadOnlySpan<Vector3> PositionsCm => _positionsCm.AsSpan(0, Count);
    public ReadOnlySpan<Quaternion> Orientations => _orientations.AsSpan(0, Count);
    public ReadOnlySpan<Vector3> LinearVelocitiesCmPerSecond => _linearVelocitiesCmPerSecond.AsSpan(0, Count);
    public ReadOnlySpan<Vector3> AngularVelocitiesRadiansPerSecond => _angularVelocitiesRadiansPerSecond.AsSpan(0, Count);
    public ReadOnlySpan<Physics3DBodyKind> BodyKinds => _bodyKinds.AsSpan(0, Count);

    internal void Set(
        int index,
        Physics3DBodyId bodyId,
        Entity entity,
        Vector3 positionCm,
        Quaternion orientation,
        Vector3 linearVelocityCmPerSecond,
        Vector3 angularVelocityRadiansPerSecond,
        Physics3DBodyKind bodyKind)
    {
        _bodyIds[index] = bodyId;
        _entities[index] = entity;
        _positionsCm[index] = positionCm;
        _orientations[index] = orientation;
        _linearVelocitiesCmPerSecond[index] = linearVelocityCmPerSecond;
        _angularVelocitiesRadiansPerSecond[index] = angularVelocityRadiansPerSecond;
        _bodyKinds[index] = bodyKind;
    }

    internal void SetCount(int count, long stepIndex)
    {
        Count = count;
        StepIndex = stepIndex;
    }
}
