namespace Ludots.Core.Modding;

public interface IRegistrySetView
{
    int RegisterGraph(string name);
    int GetGraphId(string name);
    string GetGraphName(int id);
    int RegisterTag(string name);
    int GetTagId(string name);
    string GetTagName(int id);
    int RegisterAttribute(string name);
    int GetAttributeId(string name);
    string GetAttributeName(int id);
    int RegisterAbility(string name);
    int GetAbilityId(string name);
    int RegisterEffectTemplate(string name);
    int GetEffectTemplateId(string name);
    int RegisterConfigKey(string name);
    int GetConfigKeyId(string name);
}
