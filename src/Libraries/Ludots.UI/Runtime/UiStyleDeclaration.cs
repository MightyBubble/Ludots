using System;
using System.Collections;
using System.Collections.Generic;

namespace Ludots.UI.Runtime;

public sealed class UiStyleDeclaration : IEnumerable<KeyValuePair<string, string>>, IEnumerable
{
	private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public int Count => _values.Count;

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
				throw new ArgumentException("Style property name is required.", "name");
			}
			if (string.IsNullOrWhiteSpace(value))
			{
				_values.Remove(name);
			}
			else
			{
				_values[name] = value.Trim();
			}
		}
	}

	public void Set(string name, string value)
	{
		this[name] = value;
	}

	public void Merge(UiStyleDeclaration? other)
	{
		if (other == null)
		{
			return;
		}
		foreach (KeyValuePair<string, string> item in other)
		{
			_values[item.Key] = item.Value;
		}
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
