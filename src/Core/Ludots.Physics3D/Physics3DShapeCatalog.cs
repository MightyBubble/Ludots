using System;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;

namespace Ludots.Core.Physics3D;

internal sealed class Physics3DShapeCatalog
{
    private readonly Simulation _simulation;
    private readonly TypedIndex[] _typedIndices;
    private readonly Physics3DShapeKind[] _kinds;
    private readonly Vector3[] _parameters;

    public Physics3DShapeCatalog(Simulation simulation, int capacity)
    {
        _simulation = simulation;
        Capacity = capacity;
        _typedIndices = new TypedIndex[capacity + 1];
        _kinds = new Physics3DShapeKind[capacity + 1];
        _parameters = new Vector3[capacity + 1];
    }

    public int Capacity { get; }
    public int Count { get; private set; }

    public Physics3DShapeId RegisterBox(Vector3 sizeCm)
    {
        Physics3DValidation.RequireFinite(sizeCm, nameof(sizeCm));
        Physics3DValidation.RequireFinitePositive(sizeCm.X, $"{nameof(sizeCm)}.X");
        Physics3DValidation.RequireFinitePositive(sizeCm.Y, $"{nameof(sizeCm)}.Y");
        Physics3DValidation.RequireFinitePositive(sizeCm.Z, $"{nameof(sizeCm)}.Z");
        int existingIndex = Find(Physics3DShapeKind.Box, sizeCm);
        if (existingIndex > 0)
        {
            return new Physics3DShapeId(existingIndex);
        }

        int index = Allocate(Physics3DShapeKind.Box, sizeCm);
        _typedIndices[index] = _simulation.Shapes.Add(new Box(sizeCm.X, sizeCm.Y, sizeCm.Z));
        return new Physics3DShapeId(index);
    }

    public Physics3DShapeId RegisterSphere(float radiusCm)
    {
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        var parameters = new Vector3(radiusCm, 0f, 0f);
        int existingIndex = Find(Physics3DShapeKind.Sphere, parameters);
        if (existingIndex > 0)
        {
            return new Physics3DShapeId(existingIndex);
        }

        int index = Allocate(Physics3DShapeKind.Sphere, parameters);
        _typedIndices[index] = _simulation.Shapes.Add(new Sphere(radiusCm));
        return new Physics3DShapeId(index);
    }

    public Physics3DShapeId RegisterCapsule(float radiusCm, float cylinderLengthCm)
    {
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        Physics3DValidation.RequireFiniteNonNegative(cylinderLengthCm, nameof(cylinderLengthCm));
        var parameters = new Vector3(radiusCm, cylinderLengthCm, 0f);
        int existingIndex = Find(Physics3DShapeKind.Capsule, parameters);
        if (existingIndex > 0)
        {
            return new Physics3DShapeId(existingIndex);
        }

        int index = Allocate(Physics3DShapeKind.Capsule, parameters);
        _typedIndices[index] = _simulation.Shapes.Add(new Capsule(radiusCm, cylinderLengthCm));
        return new Physics3DShapeId(index);
    }

    public Physics3DShapeId RegisterCylinder(float radiusCm, float lengthCm)
    {
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        Physics3DValidation.RequireFinitePositive(lengthCm, nameof(lengthCm));
        var parameters = new Vector3(radiusCm, lengthCm, 0f);
        int existingIndex = Find(Physics3DShapeKind.Cylinder, parameters);
        if (existingIndex > 0)
        {
            return new Physics3DShapeId(existingIndex);
        }

        int index = Allocate(Physics3DShapeKind.Cylinder, parameters);
        _typedIndices[index] = _simulation.Shapes.Add(new Cylinder(radiusCm, lengthCm));
        return new Physics3DShapeId(index);
    }

    public TypedIndex RequireTypedIndex(Physics3DShapeId id)
    {
        Require(id);
        return _typedIndices[id.Value];
    }

    public BodyInertia ComputeInertia(Physics3DShapeId id, float mass)
    {
        Require(id);
        Physics3DValidation.RequireFinitePositive(mass, nameof(mass));
        Vector3 parameters = _parameters[id.Value];
        return _kinds[id.Value] switch
        {
            Physics3DShapeKind.Box => new Box(parameters.X, parameters.Y, parameters.Z).ComputeInertia(mass),
            Physics3DShapeKind.Sphere => new Sphere(parameters.X).ComputeInertia(mass),
            Physics3DShapeKind.Capsule => new Capsule(parameters.X, parameters.Y).ComputeInertia(mass),
            Physics3DShapeKind.Cylinder => new Cylinder(parameters.X, parameters.Y).ComputeInertia(mass),
            _ => throw new InvalidOperationException($"Physics3D shape '{id}' has an unknown kind.")
        };
    }

    private int Allocate(Physics3DShapeKind kind, Vector3 parameters)
    {
        if (Count >= Capacity)
        {
            throw new Physics3DCapacityExceededException("shared shapes", Capacity);
        }

        int index = ++Count;
        _kinds[index] = kind;
        _parameters[index] = parameters;
        return index;
    }

    private int Find(Physics3DShapeKind kind, Vector3 parameters)
    {
        for (int index = 1; index <= Count; index++)
        {
            if (_kinds[index] == kind && _parameters[index] == parameters)
            {
                return index;
            }
        }

        return 0;
    }

    private void Require(Physics3DShapeId id)
    {
        if (id.Value <= 0 || id.Value > Count || !_typedIndices[id.Value].Exists)
        {
            throw new InvalidOperationException($"Physics3D shape id '{id}' is unknown.");
        }
    }
}
