using System;
using Arch.Core;

namespace Ludots.Core.EntityHistory;

public interface IEntitySnapshotReader
{
    bool TryCapture(World world, in Entity entity, int tick, out EntitySnapshot snapshot);
}

public sealed class EntitySnapshotCapture : IDisposable
{
    private readonly World _world;
    private readonly EntitySnapshotStore _store;
    private readonly IEntitySnapshotReader _reader;
    private bool _disposed;

    public EntitySnapshotCapture(World world, EntitySnapshotStore store, IEntitySnapshotReader reader)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _world.SubscribeEntityDestroyed(CaptureDestroyed);
    }

    public int CapacityRejections { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    private void CaptureDestroyed(in Entity entity)
    {
        if (_disposed || !_reader.TryCapture(_world, in entity, 0, out EntitySnapshot snapshot))
            return;

        snapshot.Identity = EntityRef.From(entity);
        snapshot.State = EntitySnapshotState.Destroyed;
        if (_store.Upsert(in snapshot) == EntityHistoryStoreResult.CapacityRejected)
            CapacityRejections++;
    }
}
