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
    /// <summary>
    ///     Its relations. 
    /// </summary>
    internal readonly SortedList<Entity, T> Elements;

    /// <summary>
    ///     Initializes a new instance of an <see cref="Relationship{T}"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Relationship()
    {
        Elements = new SortedList<Entity, T>();
    }
    
    /// <summary>
    ///     Initializes a new instance of an <see cref="Relationship{T}"/>.
    /// <remarks>Mostly for binary serialization.</remarks>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Relationship(SortedList<Entity, T> elements)
    {
        Elements = elements;
    }
    
    /// <inheritdoc/>
    int IRelationship.Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Elements.Count;
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
        Elements.Add(target, relationship);
    }
    
    /// <summary>
    ///     Sets the stored <typeparamref name="T"/> for the given <see cref="Entity"/>.
    /// </summary>
    /// <param name="entity">The <see cref="Entity"/>.</param>
    /// <param name="data">The data to store.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(Entity entity, T data = default!)
    {
        int index = FindEntityIndex(entity);
        if (index >= 0)
        {
            Elements[entity] = data;
            return;
        }

        Elements.Add(entity, data);
    }
    
    /// <summary>
    ///     Determines whether the given <see cref="Relationship{T}"/> contains the passed <see cref="Entity"/> or not.
    /// </summary>
    /// <param name="entity">The <see cref="Entity"/>.</param>
    /// <returns>True or false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Entity entity)
    {
        return FindEntityIndex(entity) >= 0;
    }
    
    /// <summary>
    ///     Returns the stored <typeparamref name="T"/> for the given <see cref="Entity"/>.
    /// </summary>
    /// <param name="entity">The <see cref="Entity"/>.</param>
    /// <returns>The stored <typeparamref name="T"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get(Entity entity)
    {
        int index = FindEntityIndex(entity);
        if (index < 0)
        {
            throw new KeyNotFoundException();
        }

        return Elements.Values[index];
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
        return TryGetValueNoAlloc(entity, out value);
    }

    /// <summary>
    ///     Returns the stored <typeparamref name="T"/> without going through
    ///     <see cref="SortedList{TKey,TValue}"/> key lookup, whose public APIs box struct keys.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetValueNoAlloc(Entity entity, out T value)
    {
        IList<Entity> keys = Elements.Keys;
        int lo = 0;
        int hi = Elements.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int comparison = keys[mid].CompareTo(entity);
            if (comparison == 0)
            {
                value = Elements.Values[mid];
                return true;
            }

            if (comparison < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        value = default!;
        return false;
    }
    
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IRelationship.Remove(Entity target)
    {
        int index = FindEntityIndex(target);
        if (index >= 0)
        {
            Elements.RemoveAt(index);
        }
    }

    /// <inheritdoc cref="IRelationship.Remove(Entity)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Remove(Entity target)
    {
        ((IRelationship) this).Remove(target);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindEntityIndex(Entity entity)
    {
        IList<Entity> keys = Elements.Keys;
        int lo = 0;
        int hi = Elements.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int comparison = keys[mid].CompareTo(entity);
            if (comparison == 0)
            {
                return mid;
            }

            if (comparison < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return -1;
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
        return new SortedListEnumerator<T>(Elements);
    }

};
