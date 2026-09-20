using Ludots.Core.Registry;

namespace Ludots.Core.Modding;

public sealed class RegistrySetView : IRegistrySetView
{
    private readonly ModRegistrySet _set;

    public RegistrySetView(ModRegistrySet set)
    {
        _set = set ?? throw new ArgumentNullException(nameof(set));
    }

    public int RegisterGraph(string name) => _set.GraphIds.Register(name);
    public int GetGraphId(string name) => _set.GraphIds.GetId(name);
    public string GetGraphName(int id) => _set.GraphIds.GetName(id);
    public int RegisterTag(string name) => _set.Tags.Register(name);
    public int GetTagId(string name) => _set.Tags.GetId(name);
    public string GetTagName(int id) => _set.Tags.GetName(id);
    public int RegisterAttribute(string name) => _set.Attributes.Register(name);
    public int GetAttributeId(string name) => _set.Attributes.GetId(name);
    public string GetAttributeName(int id) => _set.Attributes.GetName(id);
    public int RegisterAbility(string name) => _set.AbilityIds.Register(name);
    public int GetAbilityId(string name) => _set.AbilityIds.GetId(name);
    public int RegisterEffectTemplate(string name) => _set.EffectTemplateIds.Register(name);
    public int GetEffectTemplateId(string name) => _set.EffectTemplateIds.GetId(name);
    public int RegisterConfigKey(string name) => _set.ConfigKeys.Register(name);
    public int GetConfigKeyId(string name) => _set.ConfigKeys.GetId(name);
}
