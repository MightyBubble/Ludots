using System.Collections;

namespace Ludots.UI.Runtime;

public sealed class UiAttributeBag : IEnumerable<KeyValuePair<string, string>>
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public string? this[string name]
    {
        get => _values.TryGetValue(name, out string? value) ? value : null;
        set
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Attribute name is required.", nameof(name));
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

    public void Set(string name, string value)
    {
        this[name] = value;
    }

    public bool TryGetValue(string name, out string value)
    {
        return _values.TryGetValue(name, out value!);
    }

    public bool Contains(string name)
    {
        return _values.ContainsKey(name);
    }

    public IReadOnlyList<string> GetClassList()
    {
        if (!_values.TryGetValue("class", out string? classText) || string.IsNullOrWhiteSpace(classText))
        {
            return Array.Empty<string>();
        }

        return classText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
