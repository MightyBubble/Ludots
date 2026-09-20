using Arch.System;
using Ludots.Core.Engine;

namespace Ludots.Core.Modding;

public sealed class UnavailableSystemRegistrar : ISystemRegistrar
{
    public static UnavailableSystemRegistrar Instance { get; } = new();

    private UnavailableSystemRegistrar()
    {
    }

    public void RegisterSystem(ISystem<float> system, SystemGroup group)
        => throw CreateException();

    public void RegisterPresentationSystem(ISystem<float> system)
        => throw CreateException();

    public void InsertSystemBeforeRequired<TAnchor>(ISystem<float> system, SystemGroup group)
        where TAnchor : class
        => throw CreateException();

    private static InvalidOperationException CreateException()
        => new("This mod context has no system registrar. The host must bind ports before OnLoad.");
}

public sealed class UnavailableRegistrySetView : IRegistrySetView
{
    public static UnavailableRegistrySetView Instance { get; } = new();

    private UnavailableRegistrySetView()
    {
    }

    public int RegisterGraph(string name) => throw CreateException();
    public int GetGraphId(string name) => throw CreateException();
    public string GetGraphName(int id) => throw CreateException();
    public int RegisterTag(string name) => throw CreateException();
    public int GetTagId(string name) => throw CreateException();
    public string GetTagName(int id) => throw CreateException();
    public int RegisterAttribute(string name) => throw CreateException();
    public int GetAttributeId(string name) => throw CreateException();
    public string GetAttributeName(int id) => throw CreateException();
    public int RegisterAbility(string name) => throw CreateException();
    public int GetAbilityId(string name) => throw CreateException();
    public int RegisterEffectTemplate(string name) => throw CreateException();
    public int GetEffectTemplateId(string name) => throw CreateException();
    public int RegisterConfigKey(string name) => throw CreateException();
    public int GetConfigKeyId(string name) => throw CreateException();

    private static InvalidOperationException CreateException()
        => new("This mod context has no registry set. The host must bind ports before OnLoad.");
}
