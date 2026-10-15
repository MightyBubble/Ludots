using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace CrowdSimulationTests.Authoring;

/// <summary>
/// 群体单位模板到表现的链接走真实 Mod 装载：最小样板复制到临时目录，只改 presenters.json。
/// 引用不存在的表现定义在表现装载时失败；单位模板没有表现在群体会话激活时失败。
/// </summary>
[TestFixture]
public sealed class CrowdUnitPresenterContractTests
{
    private const string ModId = "CrowdSimulationMinimalMod";
    private const string MapId = "crowd_minimal";
    private const string Unit = "crowd_minimal.unit";
    private const string Bootstrap = "crowd_minimal.units.bootstrap";
    private const string PresentersSource = ModId + ":assets/Presentation/presenters.json";
    private const string TemplatesSource = ModId + ":assets/Entities/templates.json";
    private const int Units = 400;

    [Test]
    public void SpawnRuleCreatingUnknownDefinition_FailsPresenterLoad_NamingTemplateDefinitionAndFile()
    {
        string mod = CopyMinimalMod(presenters => Rule(presenters, 0)["command"]!["definitionId"] = Unit + "_typo");

        var ex = Assert.Throws<InvalidOperationException>(() => StartEngine(mod).Dispose());

        Assert.That(ex!.Message, Is.EqualTo(
            $"Presenter '{Bootstrap}' rules[0]: EntitySpawned '{Unit}' creates unknown presenter definition '{Unit}_typo'. " +
            $"Written by {PresentersSource}."));
    }

    [Test]
    public void SpawnRuleKeyedOnUnknownTemplate_FailsPresenterLoad_NamingKeyDefinitionAndFile()
    {
        string mod = CopyMinimalMod(presenters =>
        {
            Rule(presenters, 0)["event"]!["key"] = Unit + "_typo";
            Rule(presenters, 1)["event"]!["key"] = Unit + "_typo";
        });

        var ex = Assert.Throws<InvalidOperationException>(() => StartEngine(mod).Dispose());

        Assert.That(ex!.Message, Is.EqualTo(
            $"Presenter '{Bootstrap}' rules[0]: EntitySpawned key '{Unit}_typo' is not a registered entity template (creates '{Unit}'). " +
            $"Written by {PresentersSource}."));
    }

    [Test]
    public void UnitTemplateWithoutSpawnPresenter_FailsCrowdActivation_NamingTemplateAndSources()
    {
        string mod = CopyMinimalMod(presenters => presenters.RemoveAt(0));
        using GameEngine engine = StartEngine(mod);

        var ex = Assert.Throws<InvalidOperationException>(() => engine.LoadMap(MapId));

        Assert.That(ex!.Message, Is.EqualTo(
            "CrowdSimulation 会话绑定的单位模板缺表现。\n" +
            $"单位模板 \"{Unit}\"（{TemplatesSource}）生成时没有表现：Presentation/presenters.json 里没有 EntitySpawned 键为 \"{Unit}\" 或 \"*\" 的 CreatePresenter 规则，也没有表现定义用 bindSpawn.template 指向它。\n" +
            "Presentation/presenters.json 合并顺序: LudotsCoreMod:assets/Presentation/presenters.json | CrowdSimulationMod:assets/Presentation/presenters.json | " +
            PresentersSource));
    }

    [Test]
    public void SpawnRuleKeyedOnAnotherTemplate_LeavesTheUnitWithoutPresenter_FailsCrowdActivation()
    {
        string mod = CopyMinimalMod(presenters =>
        {
            Rule(presenters, 0)["event"]!["key"] = "crowd_minimal.team";
            Rule(presenters, 1)["event"]!["key"] = "crowd_minimal.team";
        });
        using GameEngine engine = StartEngine(mod);

        var ex = Assert.Throws<InvalidOperationException>(() => engine.LoadMap(MapId));

        Assert.That(ex!.Message, Does.Contain($"单位模板 \"{Unit}\"（{TemplatesSource}）生成时没有表现"));
    }

    [Test]
    public void BindSpawnOnTheDefinition_IsALink_AndEveryUnitGetsItsPresenter()
    {
        string mod = CopyMinimalMod(presenters =>
        {
            presenters.RemoveAt(0);
            presenters[0]!["bindSpawn"] = new JsonObject { ["template"] = Unit, ["scopeSource"] = "EventPayloadA" };
        });
        using GameEngine engine = StartEngine(mod);
        engine.LoadMap(MapId);

        var links = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!.SpawnLinks.For(Unit);
        Assert.That(links, Is.EqualTo(new[] { new PresenterSpawnLink(Unit, "bindSpawn", Unit, Unit, PresentersSource) }));
        CrowdSimSession session = engine.GetService(CoreServiceKeys.CrowdSimulationSession)!;
        for (int i = 0; i < 120 && session.Units.Count < Units; i++)
        {
            engine.Tick(1f / 30f);
        }

        Assert.That(session.Units.Count, Is.EqualTo(Units));
        Assert.That(UnitsWithPresenter(engine, session), Is.EqualTo(Units));
    }

    [Test]
    public void ShippedMinimalMod_LinksItsUnitThroughTheBootstrapRule()
    {
        string repo = RepoRoot();
        using GameEngine engine = StartEngine(Path.Combine(repo, "mods", "showcases", "crowd_simulation", ModId));

        var links = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!.SpawnLinks.For(Unit);

        Assert.That(links, Is.EqualTo(new[] { new PresenterSpawnLink(Bootstrap, "rules[0]", Unit, Unit, PresentersSource) }));
    }

    [Test]
    public void WildcardSpawnRule_CountsForEveryTemplate()
    {
        var keyed = new PresenterSpawnLink("owner", "rules[0]", Unit, "a", "M:f");
        var wildcard = new PresenterSpawnLink("owner", "rules[1]", PresenterSpawnLinks.WildcardTemplate, "b", "M:f");
        var links = new PresenterSpawnLinks(new[] { keyed, wildcard });

        Assert.That(links.For(Unit), Is.EqualTo(new[] { keyed, wildcard }));
        Assert.That(links.For("other"), Is.EqualTo(new[] { wildcard }));
        Assert.That(new PresenterSpawnLinks(new[] { keyed }).For("other"), Is.Empty);
    }

    private static JsonObject Rule(JsonArray presenters, int index) => presenters[0]!["rules"]![index]!.AsObject();

    private static int UnitsWithPresenter(GameEngine engine, CrowdSimSession session)
    {
        var owners = new HashSet<Entity>();
        engine.World.Query(new QueryDescription().WithAll<PresenterState>(), (ref PresenterState state) => owners.Add(state.OwnerEntity));
        int count = 0;
        for (int i = 0; i < session.Units.Count; i++)
        {
            if (owners.Contains(session.Units.EntityAt(i)))
            {
                count++;
            }
        }

        return count;
    }

    private static string CopyMinimalMod(Action<JsonArray> editPresenters)
    {
        string source = Path.Combine(RepoRoot(), "mods", "showcases", "crowd_simulation", ModId);
        string target = Path.Combine(Path.GetTempPath(), "crowd-presenter-contract-" + Guid.NewGuid().ToString("N"), ModId);
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        }

        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }

        string presentersPath = Path.Combine(target, "assets", "Presentation", "presenters.json");
        JsonArray presenters = JsonNode.Parse(File.ReadAllText(presentersPath))!.AsArray();
        editPresenters(presenters);
        File.WriteAllText(presentersPath, presenters.ToJsonString());
        return target;
    }

    [TearDown]
    public void RemoveCopies()
    {
        foreach (string dir in Directory.GetDirectories(Path.GetTempPath(), "crowd-presenter-contract-*"))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static GameEngine StartEngine(string modRoot)
    {
        string repo = RepoRoot();
        var engine = new GameEngine();
        try
        {
            engine.InitializeWithConfigPipeline(new List<string>
            {
                Path.Combine(repo, "mods", "LudotsCoreMod"),
                Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
                Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
                modRoot,
            }, Path.Combine(repo, "assets"));
            engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
            engine.SetService(CoreServiceKeys.ViewController, new StubView());
            engine.Start();
            return engine;
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    private sealed class StubView : Ludots.Core.Presentation.Camera.IViewController
    {
        public System.Numerics.Vector2 Resolution { get; } = new(1280, 720);
        public float Fov { get; } = 50f;
        public float AspectRatio { get; } = 1280f / 720f;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
