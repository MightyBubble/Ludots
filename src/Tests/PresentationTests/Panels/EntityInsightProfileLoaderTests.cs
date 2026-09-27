using System.Text.Json.Nodes;
using Arch.Core;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Insight;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class EntityInsightProfileLoaderTests
{
    private const string AbilityKey = "Tests.EntityInfo.InstanceTitle";

    private static readonly string[] TokenKeys =
    {
        "hero.shared.title",
        "hero.liu.title",
        "hero.guan.title",
        "hero.genre",
        "hero.subtitle",
        "hero.body",
        "hero.stat",
        "hero.tip",
        "hero.action.title",
        "hero.action.body",
    };

    private static readonly string[] English =
    {
        "Hero",
        "Liu Bei",
        "Guan Yu",
        "Genre",
        "Subtitle",
        "Body",
        "Stat",
        "Tip",
        "Action",
        "Action body",
    };

    private static readonly string[] Chinese =
    {
        "英雄",
        "刘备",
        "关羽",
        "体裁",
        "副题",
        "正文",
        "数值",
        "提示",
        "动作",
        "动作说明",
    };

    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "Ludots_EntityInsightProfileLoader", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        AbilityIdRegistry.Register(AbilityKey);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void Load_StoresOptionalProfileTitle()
    {
        LoadedInsight loaded = Load(HappyProfiles());

        Assert.That(loaded.Insight.TryGetProfileByTemplateKey(loaded.HeroKeyId, out EntityInsightProfile hero), Is.True);
        Assert.That(hero.TitleTokenId, Is.EqualTo(loaded.Text.GetTokenId("hero.shared.title")));
        Assert.That(loaded.Insight.TryGetProfileByTemplateKey(loaded.ScoutKeyId, out EntityInsightProfile scout), Is.True);
        Assert.That(scout.TitleTokenId, Is.EqualTo(0));
    }

    [Test]
    public void Load_UnknownTitleToken_Fails()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Load(HappyProfiles().Replace("\"hero.shared.title\"", "\"missing.token\"", StringComparison.Ordinal)))!;
        Assert.That(ex.Message, Does.Contain("unknown text token"));
        Assert.That(ex.Message, Does.Contain("missing.token"));
    }

    [Test]
    public void Refresh_MapTitleToken_FollowsActiveLocale()
    {
        LoadedInsight loaded = Load(HappyProfiles());
        var localeSelection = new PresentationTextLocaleSelection(loaded.Text);
        var service = new EntityInfoPanelService(loaded.Insight, loaded.Text, localeSelection);
        using var world = World.Create();
        Entity liu = world.Create(
            new Name { Value = "Template Hero" },
            new EntityInfoTitleToken { Value = "hero.liu.title" },
            new EntityTemplateKeyRef { TemplateKeyId = loaded.HeroKeyId });
        Entity shared = world.Create(
            new Name { Value = "Template Hero" },
            new EntityTemplateKeyRef { TemplateKeyId = loaded.HeroKeyId });

        EntityInfoPanelHandle liuPanel = Open(service, liu);
        EntityInfoPanelHandle sharedPanel = Open(service, shared);
        service.Refresh(world, new Dictionary<string, object>());

        Assert.That(service.GetTitle(liuPanel.Slot), Is.EqualTo("Liu Bei"));
        Assert.That(service.GetSubtitle(liuPanel.Slot), Is.EqualTo("Subtitle"));
        Assert.That(service.GetTitle(sharedPanel.Slot), Is.EqualTo("Hero"));
        Assert.That(world.Get<Name>(liu).Value, Is.EqualTo("Template Hero"));

        localeSelection.SetActiveLocale("zh-CN");
        service.Refresh(world, new Dictionary<string, object>());

        Assert.That(service.GetTitle(liuPanel.Slot), Is.EqualTo("刘备"));
        Assert.That(service.GetSubtitle(liuPanel.Slot), Is.EqualTo("副题"));
        Assert.That(service.GetTitle(sharedPanel.Slot), Is.EqualTo("英雄"));
        Assert.That(world.Get<Name>(liu).Value, Is.EqualTo("Template Hero"));
    }

    private static EntityInfoPanelHandle Open(EntityInfoPanelService service, Entity entity)
    {
        return service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.InsightBrief,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.Fixed(entity),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 0f, 0f, 360f, 240f),
            EntityInfoGasDetailFlags.None,
            true));
    }

    private LoadedInsight Load(string profilesJson)
    {
        WriteFile("Core", "config_catalog.json",
            """
            [
              { "Path": "Presentation/text_tokens.json", "Policy": "ArrayById", "IdField": "id" },
              { "Path": "Presentation/text_locales.json", "Policy": "DeepObject" },
              { "Path": "EntityInfo/insight_profiles.json", "Policy": "ArrayById", "IdField": "id", "AllowEmpty": true }
            ]
            """);
        WriteFile("Core", "Presentation/text_tokens.json", TokenFile());
        WriteFile("Core", "Presentation/text_locales.json", LocaleFile());
        WriteFile("Core", "EntityInfo/insight_profiles.json", profilesJson);

        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", Path.Combine(_root, "Core"));
        var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        PresentationTextCatalog text = new PresentationTextCatalogLoader(pipeline).Load(catalog);
        var templateKeys = new EntityTemplateKeyRegistry();
        int heroKeyId = templateKeys.Register("hero");
        int scoutKeyId = templateKeys.Register("scout");
        EntityInsightProfileCatalog insight = new EntityInsightProfileLoader(pipeline).Load(catalog, report: null, templateKeys, text);
        return new LoadedInsight(insight, text, heroKeyId, scoutKeyId);
    }

    private static string HappyProfiles()
    {
        return $$"""
        [
          {
            "id": "hero.profile",
            "templateIds": ["hero"],
            "titleToken": "hero.shared.title",
            "accentColorHex": "#112233",
            "surfaceColorHex": "#000000",
            "genreGlyph": "H",
            "portraitGlyph": "P",
            "genreLabelToken": "hero.genre",
            "subtitleToken": "hero.subtitle",
            "bodyToken": "hero.body",
            "stats": [
              { "glyph": "S", "labelToken": "hero.stat", "source": "constant", "value": 1, "display": "constant" }
            ],
            "tips": [ { "glyph": "T", "textToken": "hero.tip" } ],
            "actions": [
              { "ability": "{{AbilityKey}}", "glyph": "A", "titleToken": "hero.action.title", "bodyToken": "hero.action.body" }
            ]
          },
          {
            "id": "scout.profile",
            "templateIds": ["scout"],
            "accentColorHex": "#222222",
            "surfaceColorHex": "#333333",
            "genreGlyph": "S",
            "portraitGlyph": "C",
            "genreLabelToken": "hero.genre",
            "subtitleToken": "hero.subtitle",
            "bodyToken": "hero.body",
            "stats": [
              { "glyph": "S", "labelToken": "hero.stat", "source": "constant", "value": 1, "display": "constant" }
            ],
            "tips": [ { "glyph": "T", "textToken": "hero.tip" } ],
            "actions": [
              { "ability": "{{AbilityKey}}", "glyph": "A", "titleToken": "hero.action.title", "bodyToken": "hero.action.body" }
            ]
          }
        ]
        """;
    }

    private static string TokenFile()
    {
        var rows = new JsonArray();
        for (int i = 0; i < TokenKeys.Length; i++)
        {
            rows.Add(new JsonObject
            {
                ["id"] = TokenKeys[i],
                ["argCount"] = 0,
            });
        }

        return rows.ToJsonString();
    }

    private static string LocaleFile()
    {
        var en = new JsonObject();
        var zh = new JsonObject();
        for (int i = 0; i < TokenKeys.Length; i++)
        {
            en[TokenKeys[i]] = English[i];
            zh[TokenKeys[i]] = Chinese[i];
        }

        var root = new JsonObject
        {
            ["defaultLocale"] = "en-US",
            ["locales"] = new JsonObject
            {
                ["en-US"] = en,
                ["zh-CN"] = zh,
            },
        };
        return root.ToJsonString();
    }

    private void WriteFile(string modId, string relativePath, string content)
    {
        string dir = Path.Combine(_root, modId, Path.GetDirectoryName(relativePath) ?? string.Empty);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
    }

    private readonly record struct LoadedInsight(
        EntityInsightProfileCatalog Insight,
        PresentationTextCatalog Text,
        int HeroKeyId,
        int ScoutKeyId);
}
