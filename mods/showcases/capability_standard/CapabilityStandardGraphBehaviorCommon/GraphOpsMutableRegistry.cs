using Ludots.Core.Gameplay.GAS.Registry;

namespace CapabilityStandardGraphBehaviorCommon;

internal static class GraphOpsMutableRegistry
{
    public static int Tag(string name)
    {
        int id = TagRegistry.GetId(name);
        if (id > 0)
        {
            return id;
        }

        if (TagRegistry.IsFrozen)
        {
            throw new InvalidOperationException($"Graph references unknown tag '{name}'.");
        }

        return TagRegistry.Register(name);
    }

    public static int Attribute(string name)
    {
        int id = AttributeRegistry.GetId(name);
        if (id >= 0)
        {
            return id;
        }

        if (AttributeRegistry.IsFrozen)
        {
            throw new InvalidOperationException($"Graph references unknown attribute '{name}'.");
        }

        return AttributeRegistry.Register(name);
    }

    public static int EffectTemplate(string name)
    {
        int id = EffectTemplateIdRegistry.GetId(name);
        if (id > 0)
        {
            return id;
        }

        if (EffectTemplateIdRegistry.IsFrozen)
        {
            throw new InvalidOperationException($"Graph references unknown effect template '{name}'.");
        }

        return EffectTemplateIdRegistry.Register(name);
    }

    public static int ConfigKey(string name)
    {
        int id = ConfigKeyRegistry.GetId(name);
        if (id > 0)
        {
            return id;
        }

        if (ConfigKeyRegistry.IsFrozen)
        {
            throw new InvalidOperationException($"Graph references unknown config key '{name}'.");
        }

        return ConfigKeyRegistry.Register(name);
    }
}
