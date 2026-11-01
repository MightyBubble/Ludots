using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Config;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class CommandSourceCollectionKeyPatchTests
{
    [Test]
    public void UnboundRoleToken_FailsWhenPatchingWriteCollection()
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
                new[] { "role:commandSource" },
                program,
                new UnusedSymbolResolver(),
                CreateStore(),
                collectionRoles: null,
                sourceMod: "LudotsCoreMod",
                graphId: "graph.core.select_commit"));

        Assert.That(ex!.Message, Does.Contain("RoleUnbound"));
        Assert.That(ex.Message, Does.Contain("graph 'graph.core.select_commit'"));
        Assert.That(ex.Message, Does.Contain("commandSource"));
        Assert.That(ex.Message, Does.Contain("LudotsCoreMod"));
    }

    [Test]
    public void BoundRoleToken_ResolvesQueryFromCollectionKey()
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
            new[] { "role:commandSource" },
            program,
            new UnusedSymbolResolver(),
            store,
            collectionRoles: InputCollectionKeyDeclarations.ForResolvedRoles(
                store.KeyRegistry,
                new Dictionary<string, string> { ["commandSource"] = "rts.selection" }),
            sourceMod: "RtsDemoMod",
            graphId: "graph.rts.command_commit");

        Assert.That(store.KeyRegistry.GetName(program[0].Imm), Is.EqualTo("rts.selection"));
    }

    [Test]
    public void UnknownRoleToken_FailsNamingTheRole()
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

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
            GraphProgramSymbolPatcher.Patch(
                new[] { "role:notDeclared" },
                program,
                new UnusedSymbolResolver(),
                store,
                collectionRoles: InputCollectionKeyDeclarations.ForResolvedRoles(
                    store.KeyRegistry,
                    new Dictionary<string, string> { ["commandSource"] = "rts.selection" }),
                sourceMod: "CameraShowcaseMod",
                graphId: "graph.camera.follow"));

        Assert.That(ex!.Message, Does.Contain("RoleUnbound"));
        Assert.That(ex.Message, Does.Contain("notDeclared"));
        Assert.That(ex.Message, Does.Contain("graph 'graph.camera.follow'"));
        Assert.That(ex.Message, Does.Contain("CameraShowcaseMod"));
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
            collectionRoles: InputCollectionKeyDeclarations.ForResolvedRoles(
                store.KeyRegistry,
                new Dictionary<string, string> { ["commandSource"] = "rts.selection" }),
            graphId: "graph.aim.enter");

        Assert.That(store.KeyRegistry.GetName(program[0].Imm), Is.EqualTo("aim.actors"));
    }

    [Test]
    public void DisagreeingRoleDeclarations_FailLoad()
    {
        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
            LoadRoles(
                ("ModA", """{"commandSource":"rts.selection"}"""),
                ("ModB", """{"commandSource":"moba.selection"}""")));

        Assert.That(ex!.Message, Does.Contain("RoleConflict"));
        Assert.That(ex.Message, Does.Contain("commandSource"));
        Assert.That(ex.Message, Does.Contain("rts.selection"));
        Assert.That(ex.Message, Does.Contain("moba.selection"));
        Assert.That(ex.Message, Does.Contain("ModA"));
        Assert.That(ex.Message, Does.Contain("ModB"));
    }

    [Test]
    public void NoDeclaredRoles_LeaveCastRawAndHoverUnbound()
    {
        InputCollectionKeyDeclarations declarations = LoadRoles();
        InvalidOperationException? castRaw = Assert.Throws<InvalidOperationException>(() => declarations.RequireRole("castRaw"));
        InvalidOperationException? hover = Assert.Throws<InvalidOperationException>(() => declarations.RequireRole("hover"));
        Assert.That(castRaw!.Message, Does.Contain("RoleUnbound"));
        Assert.That(hover!.Message, Does.Contain("RoleUnbound"));
    }

    [Test]
    public void MissingCollectionKeyFiles_LoadAnEmptyRoleTable()
    {
        InputCollectionKeyDeclarations declarations = LoadRoles(includeCoreFile: false);
        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => declarations.RequireRole("hover"));
        Assert.That(ex!.Message, Does.Contain("RoleUnbound"));
        Assert.That(ex.Message, Does.Contain("hover"));
    }

    [Test]
    public void SameRoleValueDeclaredTwice_Loads()
    {
        InputCollectionKeyDeclarations declarations = LoadRoles(
            ("ModA", """{"commandSource":"rts.selection"}"""),
            ("ModB", """{"commandSource":"rts.selection"}"""));

        Assert.That(declarations.RequireRole("commandSource"), Is.EqualTo("rts.selection"));
    }

    [Test]
    [NonParallelizable]
    public void BootWithoutCommandSourceDeclaration_FailsRoleUnbound()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        try
        {
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                engine.InitializeWithConfigPipeline(
                    RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                    Path.Combine(repoRoot, "assets"),
                    Ludots.Tests.EngineUserData.CreateIsolatedRoot()));

            string message = ex!.Message;
            Assert.That(message, Does.Contain("INPUT.COLLECTION_KEYS.ERR.RoleUnbound"));
            Assert.That(message, Does.Contain("commandSource"));
            Assert.That(message, Does.Contain("LudotsCoreMod"));
            Assert.That(message, Does.Contain("graph '"));
        }
        finally
        {
            engine.Dispose();
        }
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "src", "Core", "Ludots.Core.csproj")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }

    private static InputCollectionKeyDeclarations LoadRoles(params (string ModId, string Json)[] mods)
        => LoadRoles(includeCoreFile: true, mods);

    private static InputCollectionKeyDeclarations LoadRoles(
        bool includeCoreFile,
        params (string ModId, string Json)[] mods)
    {
        string root = Path.Combine(Path.GetTempPath(), "ludots-collection-roles", Guid.NewGuid().ToString("N"));
        try
        {
            string core = Path.Combine(root, "core");
            Directory.CreateDirectory(Path.Combine(core, "Input"));
            File.WriteAllText(
                Path.Combine(core, "config_catalog.json"),
                """[{"Path":"Input/collection_keys.json","Policy":"DeepObject"}]""");
            if (includeCoreFile)
            {
                File.WriteAllText(Path.Combine(core, "Input", "collection_keys.json"), "{}");
            }

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            foreach ((string modId, string json) in mods)
            {
                string modRoot = Path.Combine(root, modId);
                Directory.CreateDirectory(Path.Combine(modRoot, "assets", "Input"));
                File.WriteAllText(Path.Combine(modRoot, "assets", "Input", "collection_keys.json"), json);
                vfs.Mount(modId, modRoot);
                modLoader.LoadedModIds.Add(modId);
            }

            var pipeline = new ConfigPipeline(vfs, modLoader);
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
            return InputCollectionKeyDeclarations.LoadAndRegister(
                pipeline,
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                catalog);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
