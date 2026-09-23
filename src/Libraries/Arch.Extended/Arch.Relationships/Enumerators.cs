using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using CommunityToolkit.HighPerformance;

namespace Arch.Relationships;

/// <summary>
///     The <see cref="SortedListEnumerator{TValue}"/> struct
///     is a enumerator to enumerate a passed <see cref="SortedList{TKey,TValue}"/> in an efficient way. 
/// </summary>
/// <typeparam name="TValue"></typeparam>
public struct SortedListEnumerator<TValue> 
{
    private readonly Entity[] targets;
    private readonly TValue[] values;
    private readonly int count;
    private int currentIndex;

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="list">List.</param>
    public SortedListEnumerator(SortedList<Entity, TValue> list)
    {
        targets = new Entity[list.Count];
        values = new TValue[list.Count];
        count = list.Count;
        int index = 0;
        foreach (KeyValuePair<Entity, TValue> pair in list)
        {
            targets[index] = pair.Key;
            values[index] = pair.Value;
            index++;
        }

        currentIndex = -1;
    }

    internal SortedListEnumerator(Entity[] targets, TValue[] values, int count)
    {
        this.targets = targets;
        this.values = values;
        this.count = count;
        currentIndex = -1;
    }

    /// <summary>
    /// Current.
    /// </summary>
    public KeyValuePair<Entity, TValue> Current
    {
        get
        {
            if (currentIndex == -1 || currentIndex >= count)
                throw new InvalidOperationException();
                
            return new KeyValuePair<Entity, TValue>(targets[currentIndex], values[currentIndex]);
        }
    }
    
    /// <summary>
    /// Moves to the next element in the enumerator.
    /// </summary>
    public bool MoveNext()
    {
        currentIndex++;
        return currentIndex < count;
    }

    /// <summary>
    /// Resets the enumerator to its initial position.
    /// </summary>
    public void Reset()
    {
        currentIndex = -1;
    }
}
