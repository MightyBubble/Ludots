using System.Numerics;

namespace Ludots.Core.Physics3D;

/// <summary>
/// Fixed-capacity work submitted to the Physics3D read-query workers.
/// Implementations must keep each item independent because items may execute concurrently.
/// </summary>
public interface IPhysics3DParallelQueryBatch
{
    int ItemCount { get; }

    void Execute(int itemIndex, Physics3DReadQueryContext context);
}

/// <summary>
/// Worker-private Physics3D query state. Instances are owned and scheduled by the world.
/// </summary>
public sealed class Physics3DReadQueryContext
{
    private readonly Physics3DQueryEngine _queries;
    private readonly BepuUtilities.Memory.BufferPool _pool;
    private readonly Physics3DOverlapHit[] _overlapHits;

    internal Physics3DReadQueryContext(
        Physics3DQueryEngine queries,
        BepuUtilities.Memory.BufferPool pool,
        int bodySlotCapacity)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        if (bodySlotCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bodySlotCapacity));
        }

        _overlapHits = new Physics3DOverlapHit[bodySlotCapacity];
    }

    public int OverlapCapacity => _overlapHits.Length;

    public ReadOnlySpan<Physics3DOverlapHit> OverlapSphere(
        Vector3 centerCm,
        float radiusCm,
        Physics3DQueryFilter filter)
    {
        int count = _queries.OverlapSphere(
            centerCm,
            radiusCm,
            in filter,
            _pool,
            _overlapHits);
        return _overlapHits.AsSpan(0, count);
    }
}
