using System.Reflection;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance;

[Category("ci-gate")]
[Category("arch-guard")]
public sealed class S14RegistryIsolationTests
{
    [TearDown]
    public void TearDown()
    {
        ModRegistryAmbient.Reset();
    }

    [Test]
    public void TwoRegistrySets_SameGraphName_DoNotShareIdentity()
    {
        var first = new ModRegistrySet();
        var second = new ModRegistrySet();

        int firstId = first.GraphIds.Register("Graph.Shared.Name");
        int secondId = second.GraphIds.Register("Graph.Shared.Name");
        first.GraphIds.Register("Graph.Only.First");

        Assert.That(firstId, Is.EqualTo(1));
        Assert.That(secondId, Is.EqualTo(1));
        Assert.That(first.GraphIds.Count, Is.EqualTo(2));
        Assert.That(second.GraphIds.Count, Is.EqualTo(1));
        Assert.That(second.GraphIds.GetId("Graph.Only.First"), Is.EqualTo(second.GraphIds.InvalidId));
    }

    [Test]
    public void Freeze_ThenRegister_Throws_AndHasNoUnfreezeApi()
    {
        var set = new ModRegistrySet();
        set.GraphIds.Register("Graph.Frozen");
        set.FreezeAll();

        Assert.That(set.GraphIds.IsFrozen, Is.True);
        Assert.Throws<InvalidOperationException>(() => set.GraphIds.Register("Graph.Frozen"));
        Assert.Throws<InvalidOperationException>(() => set.GraphIds.Register("Graph.Late"));

        Type tableType = typeof(IdentityTable);
        Assert.That(tableType.GetMethod("Unfreeze", BindingFlags.Public | BindingFlags.Instance), Is.Null);
        Assert.That(tableType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance), Is.Null);
        Assert.That(typeof(ModRegistrySet).GetMethod("Unfreeze", BindingFlags.Public | BindingFlags.Instance), Is.Null);
        Assert.That(typeof(ModRegistrySet).GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance), Is.Null);
    }

    [Test]
    public void FacadeClear_ReplacesTable_DoesNotUnfreezeTheSameInstance()
    {
        IdentityTable before = ModRegistryAmbient.Current.GraphIds;
        before.Register("Graph.Before");
        before.Freeze();

        GraphIdRegistry.Clear();

        Assert.That(before.IsFrozen, Is.True);
        Assert.That(ReferenceEquals(before, ModRegistryAmbient.Current.GraphIds), Is.False);
        Assert.That(GraphIdRegistry.IsFrozen, Is.False);
        Assert.That(GraphIdRegistry.Register("Graph.After"), Is.EqualTo(1));
        Assert.That(before.GetId("Graph.After"), Is.EqualTo(before.InvalidId));
    }

    [Test]
    public void LoadIdsAndCompile_FailsClosed_WhenGraphTableAlreadyHasIds()
    {
        GraphIdRegistry.Register("Graph.Already.There");

        var vfs = new VirtualFileSystem();
        var loader = new GraphProgramConfigLoader(
            new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager())),
            new GraphProgramRegistry(),
            new RejectingSymbolResolver());

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => loader.LoadIdsAndCompile(relativePath: "GAS/graphs.json"))!;
        Assert.That(ex.Message, Does.Contain("not empty"));
    }

    [Test]
    public void RegistrySetView_WritesTheBoundSet_NotAmbient()
    {
        var bound = new ModRegistrySet();
        var ambient = new ModRegistrySet();
        ModRegistryAmbient.Bind(ambient);

        var view = new RegistrySetView(bound);
        int id = view.RegisterGraph("Graph.Bound.Only");

        Assert.That(id, Is.EqualTo(1));
        Assert.That(bound.GraphIds.GetId("Graph.Bound.Only"), Is.EqualTo(1));
        Assert.That(ambient.GraphIds.GetId("Graph.Bound.Only"), Is.EqualTo(ambient.GraphIds.InvalidId));
        Assert.That(GraphIdRegistry.GetId("Graph.Bound.Only"), Is.EqualTo(GraphIdRegistry.InvalidId));
    }

    [Test]
    public void UnavailablePorts_Throw_InsteadOfNoOp()
    {
        Assert.Throws<InvalidOperationException>(
            () => UnavailableSystemRegistrar.Instance.RegisterPresentationSystem(null!));
        Assert.Throws<InvalidOperationException>(
            () => UnavailableRegistrySetView.Instance.RegisterTag("tag.x"));
    }

    private sealed class RejectingSymbolResolver : IGraphSymbolResolver
    {
        public int ResolveTag(string name) => throw new NotSupportedException();
        public int ResolveAttribute(string name) => throw new NotSupportedException();
        public int ResolveEffectTemplate(string name) => throw new NotSupportedException();
        public int ResolveRelationshipType(string name) => throw new NotSupportedException();
        public int ResolveRelationshipMetric(string name) => throw new NotSupportedException();
        public int ResolveRelationshipFlag(string name) => throw new NotSupportedException();
        public int ResolveRelationshipReason(string name) => throw new NotSupportedException();
        public int ResolveTargetDispatchPreset(string name) => throw new NotSupportedException();
        public int ResolveEntityTemplate(string name) => throw new NotSupportedException();
    }
}
