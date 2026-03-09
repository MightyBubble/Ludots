using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

public sealed class UiTransform : IEquatable<UiTransform>
{
	private readonly UiTransformOperation[] _operations;

	public static UiTransform Identity { get; } = new UiTransform(Array.Empty<UiTransformOperation>());

	public IReadOnlyList<UiTransformOperation> Operations => _operations;

	public bool HasOperations => _operations.Length != 0;

	public UiTransform(IEnumerable<UiTransformOperation> operations)
	{
		ArgumentNullException.ThrowIfNull(operations, "operations");
		_operations = operations.ToArray();
	}

	public UiTransform Append(UiTransformOperation operation)
	{
		UiTransformOperation[] array = new UiTransformOperation[_operations.Length + 1];
		Array.Copy(_operations, array, _operations.Length);
		array[^1] = operation;
		return new UiTransform(array);
	}

	public bool Equals(UiTransform? other)
	{
		if (this == other)
		{
			return true;
		}
		if (other == null || _operations.Length != other._operations.Length)
		{
			return false;
		}
		for (int i = 0; i < _operations.Length; i++)
		{
			if (_operations[i] != other._operations[i])
			{
				return false;
			}
		}
		return true;
	}

	public override bool Equals(object? obj)
	{
		return obj is UiTransform other && Equals(other);
	}

	public override int GetHashCode()
	{
		HashCode hashCode = default(HashCode);
		for (int i = 0; i < _operations.Length; i++)
		{
			hashCode.Add(_operations[i]);
		}
		return hashCode.ToHashCode();
	}

	public override string ToString()
	{
		return (_operations.Length == 0) ? "none" : string.Join(' ', _operations.Select((UiTransformOperation operation) => operation.ToString()));
	}
}
