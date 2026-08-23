using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Registry;

public sealed class ModRegistrySet
{
    public ModRegistrySet()
    {
        GraphIds = CreateGraphIds();
        Tags = CreateTags();
        Attributes = CreateAttributes();
        AttributeConstraints = new AttributeRegistry.AttributeConstraints[AttributeRegistry.MaxAttributes];
        AbilityIds = CreateAbilityIds();
        EffectTemplateIds = CreateEffectTemplateIds();
        ConfigKeys = CreateConfigKeys();
    }

    public IdentityTable GraphIds { get; private set; }
    public IdentityTable Tags { get; private set; }
    public IdentityTable Attributes { get; private set; }
    public AttributeRegistry.AttributeConstraints[] AttributeConstraints { get; private set; }
    public IdentityTable AbilityIds { get; private set; }
    public IdentityTable EffectTemplateIds { get; private set; }
    public IdentityTable ConfigKeys { get; private set; }
    public bool IsFrozen { get; private set; }

    public void FreezeAll()
    {
        GraphIds.Freeze();
        Tags.Freeze();
        Attributes.Freeze();
        AbilityIds.Freeze();
        EffectTemplateIds.Freeze();
        ConfigKeys.Freeze();
        IsFrozen = true;
    }

    public void RequireGraphIdsEmptyAndUnfrozen()
    {
        if (GraphIds.IsFrozen)
        {
            throw new InvalidOperationException(
                "Graph identity table is frozen. Create a new ModRegistrySet; there is no unfreeze.");
        }

        if (GraphIds.Count > 0)
        {
            throw new InvalidOperationException(
                "Graph identity table is not empty. Create a new ModRegistrySet instead of clearing.");
        }
    }

    public void ReplaceGraphIds() => GraphIds = CreateGraphIds();
    public void ReplaceTags() => Tags = CreateTags();
    public void ReplaceAttributes()
    {
        Attributes = CreateAttributes();
        AttributeConstraints = new AttributeRegistry.AttributeConstraints[AttributeRegistry.MaxAttributes];
    }
    public void ReplaceAbilityIds() => AbilityIds = CreateAbilityIds();
    public void ReplaceEffectTemplateIds() => EffectTemplateIds = CreateEffectTemplateIds();
    public void ReplaceConfigKeys() => ConfigKeys = CreateConfigKeys();

    private static IdentityTable CreateGraphIds()
        => new("GraphId", maxExclusive: 4096, startId: 1, invalidId: 0);

    private static IdentityTable CreateTags()
        => new(
            "Tag",
            maxExclusive: 256,
            startId: 1,
            invalidId: 0,
            comparer: StringComparer.Ordinal);

    private static IdentityTable CreateAttributes()
        => new(
            "Attribute",
            maxExclusive: AttributeRegistry.MaxAttributes,
            startId: 0,
            invalidId: -1,
            comparer: StringComparer.Ordinal,
            frozenRegister: FrozenRegisterBehavior.ReturnExisting);

    private static IdentityTable CreateAbilityIds()
        => new("AbilityId", maxExclusive: 4096, startId: 1, invalidId: 0);

    private static IdentityTable CreateEffectTemplateIds()
        => new("EffectTemplateId", maxExclusive: 4096, startId: 1, invalidId: 0);

    private static IdentityTable CreateConfigKeys()
        => new("ConfigKey", maxExclusive: 4096, startId: 1, invalidId: 0);
}
