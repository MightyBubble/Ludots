using System.Runtime.CompilerServices;
using Arch.Core;

namespace Arch.Relationships;

/// <summary>
///     The <see cref="IRelationship"/> interface
///     is an interface that provides all methods required to act as a relationship.
/// </summary>
internal interface IRelationship
{
    /// <summary>
    ///     The amount of relationships currently in the buffer.
    /// </summary>
    int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    /// <summary>
    ///     Removes the buffer as a component from the given world and entity.
    /// </summary>
    /// <param name="world"></param>
    /// <param name="source"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Destroy(World world, Entity source);

    /// <summary>
    ///     Removes the relationship targeting <paramref name="target"/> from this buffer.
    /// </summary>
    /// <param name="target">The <see cref="Entity"/> in the relationship to remove.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Remove(Entity target);
}

/// <summary>
///     A buffer storing relationships of <see cref="Entity"/> and <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of the second relationship element.</typeparam>
public class Relationship<T> : IRelationship
{
    private Entity[] _targets;
    private T[] _values;
    private int _count;

    /// <summary>
    ///     Snapshot view kept for tests and debug-only relationship cleanup paths.
    ///     Hot runtime code uses the SoA arrays directly through Relationship methods.
    /// </summary>
    internal SortedList<Entity, T> Elements
    {
        get
        {
            var elements = new SortedList<Entity, T>(_count, EntityRelationshipComparer.Instance);
            for (int i = 0; i < _count; i++)
            {
                elements.Add(_targets[i], _values[i]);
            }

            return elements;
        }
    }

    /// <summary>
    ///     Initializes a new instance of an <see cref="Relationship{T}"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Relationship()
    {
        _targets = Array.Empty<Entity>();
        _values = Array.Empty<T>();
        _count = 0;
    }
    
    /// <summary>
    ///     Initializes a new instance of an <see cref="Relationship{T}"/>.
    /// <remarks>Mostly for binary serialization.</remarks>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Relationship(SortedList<Entity, T> elements)
    {
        _count = elements?.Count ?? 0;
        if (_count == 0)
        {
            _targets = Array.Empty<Entity>();
            _values = Array.Empty<T>();
            return;
        }

        _targets = new Entity[_count];
        _values = new T[_count];
        int index = 0;
        foreach (KeyValuePair<Entity, T> pair in elements!)
        {
            _targets[index] = pair.Key;
            _values[index] = pair.Value;
            index++;
        }
    }
    
    /// <inheritdoc/>
    int IRelationship.Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count;
    }

    /// <inheritdoc cref="IRelationship.Count"/>
    internal int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ((IRelationship) this).Count;
    }

    /// <summary>
    ///     Adds a relationship to this buffer.
    /// </summary>
    /// <param name="relationship">The instance of the relationship.</param>
    /// <param name="target">The target of the relationship.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Add(in T relationship, Entity target)
    {
        int index = FindIndex(target);
        if (index >= 0)
        {
            throw new ArgumentException("An item with the same entity target has already been added.", nameof(target));
        }

        Insert(~index, target, relationship);
    }
    
    /// <summary>
    ///     Sets the stored <typeparamref name="T"/> for the given <see cref="Entity"/>.
    /// </summary>
    /// <param name="entity">The <see cref="Entity"/>.</param>
    /// <param name="data">The data to store.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(Entity entity, T data = default!)
    {
        int index = FindIndex(entity);
        if (index >= 0)
        {
            _values[index] = data;
            return;
        }

        Insert(~index, entity, data);
    }
    
    /// <summary>
    ///     Determines whether the given <see cref="Relationship{T}"/> contains the passed <see cref="Entity"/> or not.
    /// </summary>
    /// <param name="entity">The <see cref="Entity"/>.</param>
    /// <returns>True or false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Entity entity)
    {
        return FindIndex(entity) >= 0;
    }
    
    /// <summary>
    ///     Returns the stored <typeparamref name="T"/> for the given <see cref="Entity"/>.
    /// </summary>
    /// <param name="entity">The <see cref="Entity"/>.</param>
    /// <returns>The stored <typeparamref name="T"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get(Entity entity)
    {
        int index = FindIndex(entity);
        if (index < 0)
        {
            throw new KeyNotFoundException("The relationship target was not present in the relationship buffer.");
        }

        return _values[index];
    }

    /// <summary>
    ///     Returns the stored <typeparamref name="T"/> for the given <see cref="Entity"/>.
    /// </summary>
    /// <param name="entity">The <see cref="Entity"/>.</param>
    /// <param name="value">The stored <typeparamref name="T"/>.</param>
    /// <returns>The stored <typeparamref name="T"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(Entity entity, out T value)
    {
        int index = FindIndex(entity);
        if (index < 0)
        {
            value = default!;
            return false;
        }

        value = _values[index];
        return true;
    }
    
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IRelationship.Remove(Entity target)
    {
        int index = FindIndex(target);
        if (index < 0)
        {
            return;
        }

        int moveCount = _count - index - 1;
        if (moveCount > 0)
        {
            Array.Copy(_targets, index + 1, _targets, index, moveCount);
            Array.Copy(_values, index + 1, _values, index, moveCount);
        }

        _count--;
        _targets[_count] = default;
        _values[_count] = default!;
    }

    /// <inheritdoc cref="IRelationship.Remove(Entity)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Remove(Entity target)
    {
        ((IRelationship) this).Remove(target);
    }
    
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IRelationship.Destroy(World world, Entity source)
    {
        world.Remove<Relationship<T>>(source);
    }

    /// <inheritdoc cref="IRelationship.Destroy(World, Entity)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Destroy(World world, Entity source)
    {
        ((IRelationship) this).Destroy(world, source);
    }

    /// <summary>
    ///     Creates a new <see cref="SortedListEnumerator{TValue}"/>.
    /// </summary>
    /// <returns>The new <see cref="SortedListEnumerator{TValue}"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SortedListEnumerator<T> GetEnumerator()
    {
        return new SortedListEnumerator<T>(_targets, _values, _count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindIndex(Entity target)
    {
        int low = 0;
        int high = _count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int compare = EntityRelationshipComparer.CompareEntities(_targets[middle], target);
            if (compare == 0)
            {
                return middle;
            }

            if (compare < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return ~low;
    }

    private void Insert(int index, Entity target, in T value)
    {
        EnsureCapacity(_count + 1);
        int moveCount = _count - index;
        if (moveCount > 0)
        {
            Array.Copy(_targets, index, _targets, index + 1, moveCount);
            Array.Copy(_values, index, _values, index + 1, moveCount);
        }

        _targets[index] = target;
        _values[index] = value;
        _count++;
    }

    private void EnsureCapacity(int requiredCount)
    {
        if (_targets.Length >= requiredCount)
        {
            return;
        }

        int next = Math.Max(4, _targets.Length * 2);
        if (next < requiredCount)
        {
            next = requiredCount;
        }

        Array.Resize(ref _targets, next);
        Array.Resize(ref _values, next);
    }
};

internal sealed class EntityRelationshipComparer : IComparer<Entity>
{
    public static readonly EntityRelationshipComparer Instance = new();

    private EntityRelationshipComparer()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Compare(Entity left, Entity right)
    {
        return CompareEntities(left, right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CompareEntities(Entity left, Entity right)
    {
        int c = left.WorldId.CompareTo(right.WorldId);
        if (c != 0)
        {
            return c;
        }

        c = left.Id.CompareTo(right.Id);
        if (c != 0)
        {
            return c;
        }

        return left.Version.CompareTo(right.Version);
    }
}
