using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS;

public sealed class DirtyEntityQueue
{
    public const string CapacityExceededError = "GAS.DIRTY_ENTITY.ERR.CapacityExceeded";

    private readonly Entity[] _items;
    private int _head;
    private int _tail;
    private int _count;

    public DirtyEntityQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "capacity must be positive.");
        }

        _items = new Entity[capacity];
    }

    public int Count => _count;
    public int Capacity => _items.Length;
    public int HighWatermark { get; private set; }
    public long OverflowCount { get; private set; }

    public bool Track(World world, Entity entity)
    {
        if (!world.IsAlive(entity) || !world.Has<DirtyFlags>(entity))
        {
            throw new InvalidOperationException(TagOps.MissingDirtyFlagsError);
        }

        ref DirtyFlags dirty = ref world.Get<DirtyFlags>(entity);
        if (dirty.DeferredTriggerQueued != 0)
        {
            return false;
        }

        if (_count >= _items.Length)
        {
            OverflowCount++;
            throw new InvalidOperationException(
                $"{CapacityExceededError}: capacity={_items.Length}, entity={entity.Id}.");
        }

        _items[_tail] = entity;
        _tail = (_tail + 1) % _items.Length;
        _count++;
        dirty.DeferredTriggerQueued = 1;
        if (_count > HighWatermark)
        {
            HighWatermark = _count;
        }
        return true;
    }

    public bool TryDequeue(out Entity entity)
    {
        if (_count == 0)
        {
            entity = default;
            return false;
        }

        entity = _items[_head];
        _head = (_head + 1) % _items.Length;
        _count--;
        return true;
    }
}
