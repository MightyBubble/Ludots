using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

public sealed class UiAttributeBag : IEnumerable<KeyValuePair<string, string>>, IEnumerable
{
	private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public string? this[string name]
	{
		get
		{
			string value;
			return _values.TryGetValue(name, out value) ? value : null;
		}
		set
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentException("Attribute name is required.", "name");
			}
			if (value == null)
			{
				_values.Remove(name);
			}
			else
			{
				_values[name] = value;
			}
		}
	}

	public int Count => _values.Count;

	public void Clear()
	{
		_values.Clear();
	}

	public void CopyFrom(UiAttributeBag other)
	{
		ArgumentNullException.ThrowIfNull(other, "other");
		_values.Clear();
		foreach (KeyValuePair<string, string> item in other._values)
		{
			_values[item.Key] = item.Value;
		}
	}

	public void Set(string name, string value)
	{
		this[name] = value;
	}

	public bool TryGetValue(string name, out string value)
	{
		return _values.TryGetValue(name, out value);
	}

	public bool Contains(string name)
	{
		return _values.ContainsKey(name);
	}

	public IReadOnlyList<string> GetClassList()
	{
		if (!_values.TryGetValue("class", out string value) || string.IsNullOrWhiteSpace(value))
		{
			return Array.Empty<string>();
		}
		return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
	{
		return _values.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}
