using System;
using System.Collections.Generic;

namespace Ludots.UI.Runtime;

public sealed class UiAnimationSpec : IEquatable<UiAnimationSpec>
{
	public IReadOnlyList<UiAnimationEntry> Entries { get; }

	public UiAnimationSpec(IReadOnlyList<UiAnimationEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries, "entries");
		Entries = entries;
	}

	public bool Equals(UiAnimationSpec? other)
	{
		if (this == other)
		{
			return true;
		}
		if (other == null || Entries.Count != other.Entries.Count)
		{
			return false;
		}
		for (int i = 0; i < Entries.Count; i++)
		{
			if (!object.Equals(Entries[i], other.Entries[i]))
			{
				return false;
			}
		}
		return true;
	}

	public override bool Equals(object? obj)
	{
		return obj is UiAnimationSpec other && Equals(other);
	}

	public override int GetHashCode()
	{
		HashCode hashCode = default(HashCode);
		for (int i = 0; i < Entries.Count; i++)
		{
			hashCode.Add(Entries[i]);
		}
		return hashCode.ToHashCode();
	}
}
