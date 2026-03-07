namespace Ludots.UI.Runtime;

public sealed class UiStyleDeclaration : IEnumerable<KeyValuePair<string, string>>
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _values.Count;

    public string? this[string name]
    {
        get => _values.TryGetValue(name, out string? value) ? value : null;
        set
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Style property name is required.", nameof(name));
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

        foreach (KeyValuePair<string, string> pair in other)
        {
            _values[pair.Key] = pair.Value;
        }
    }

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
    {
        return _values.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
