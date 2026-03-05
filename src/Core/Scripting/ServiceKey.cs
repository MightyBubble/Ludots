namespace Ludots.Core.Scripting
{
    /// <summary>
    /// A typed key for <see cref="ScriptContext"/> and GlobalContext lookups.
    /// Binds the key string to a value type at compile time, preventing
    /// type mismatches that the old string-based API could not catch.
    /// </summary>
    public readonly struct ServiceKey<T>
    {
        public string Name { get; }
        public ServiceKey(string name) => Name = name;
        public override string ToString() => $"ServiceKey<{typeof(T).Name}>(\"{Name}\")";
    }
}
