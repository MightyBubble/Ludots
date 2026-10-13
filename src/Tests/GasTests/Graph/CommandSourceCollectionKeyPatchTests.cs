using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Config;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class CommandSourceCollectionKeyPatchTests
{
    [Test]
    public void UnboundCommandSourceRole_FailsWhenPatchingWriteCollection()
    {
        GraphInstruction[] program =
        {
            new()
            {
                Op = (ushort)GraphNodeOp.WriteCollection,
                Imm = 0,
            },
        };

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
            GraphProgramSymbolPatcher.Patch(
                new[] { InputCollectionKeyDeclarations.CommandSourceRole },
                program,
                new UnusedSymbolResolver(),
                CreateStore()));

        Assert.That(ex!.Message, Does.Contain("CommandSourceUnbound"));
    }

    [Test]
    public void BoundCommandSourceRole_ResolvesQueryFromCollectionKey()
    {
        EntityCollectionStore store = CreateStore();
        GraphInstruction[] program =
        {
            new()
            {
                Op = (ushort)GraphNodeOp.QueryFromCollection,
                Imm = 0,
            },
        };

        GraphProgramSymbolPatcher.Patch(
            new[] { InputCollectionKeyDeclarations.CommandSourceRole },
            program,
            new UnusedSymbolResolver(),
            store,
            commandSourceCollectionKey: "rts.selection");

        Assert.That(store.KeyRegistry.GetName(program[0].Imm), Is.EqualTo("rts.selection"));
    }

    [Test]
    public void ConcreteCollectionKey_IsNotRewrittenToTheBoundRole()
    {
        EntityCollectionStore store = CreateStore();
        GraphInstruction[] program =
        {
            new()
            {
                Op = (ushort)GraphNodeOp.WriteCollection,
                Imm = 0,
            },
        };

        GraphProgramSymbolPatcher.Patch(
            new[] { "aim.actors" },
            program,
            new UnusedSymbolResolver(),
            store,
            commandSourceCollectionKey: "rts.selection");

        Assert.That(store.KeyRegistry.GetName(program[0].Imm), Is.EqualTo("aim.actors"));
    }

    private static EntityCollectionStore CreateStore()
    {
        return new EntityCollectionStore(
            new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
            initialCollectionCapacity: 2,
            initialRowCapacity: 4);
    }

    private sealed class UnusedSymbolResolver : IGraphSymbolResolver
    {
        public int ResolveTag(string name) => throw new InvalidOperationException(name);
        public int ResolveAttribute(string name) => throw new InvalidOperationException(name);
        public int ResolveEffectTemplate(string name) => throw new InvalidOperationException(name);
        public int ResolveRelationshipType(string name) => throw new InvalidOperationException(name);
        public int ResolveRelationshipMetric(string name) => throw new InvalidOperationException(name);
        public int ResolveRelationshipFlag(string name) => throw new InvalidOperationException(name);
        public int ResolveTargetDispatchPreset(string name) => throw new InvalidOperationException(name);
        public int ResolveEntityTemplate(string name) => throw new InvalidOperationException(name);
    }
}
