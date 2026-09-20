namespace Ludots.Core.Registry;

public static class ModRegistryAmbient
{
    private static ModRegistrySet _current = new();

    public static ModRegistrySet Current => _current;

    public static void Bind(ModRegistrySet set)
    {
        _current = set ?? throw new ArgumentNullException(nameof(set));
    }

    public static void Reset()
    {
        _current = new ModRegistrySet();
    }
}
