namespace Ludots.Contracts;

public enum LayerOwner : byte
{
    Simulation = 1,
    Presentation = 2,
}

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Field, Inherited = false)]
public sealed class WriteOwnerAttribute : Attribute
{
    public WriteOwnerAttribute(LayerOwner owner)
    {
        Owner = owner;
    }

    public LayerOwner Owner { get; }
}

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class ReadAllowedAttribute : Attribute
{
    public ReadAllowedAttribute(params LayerOwner[] readers)
    {
        Readers = readers ?? Array.Empty<LayerOwner>();
    }

    public LayerOwner[] Readers { get; }
}
