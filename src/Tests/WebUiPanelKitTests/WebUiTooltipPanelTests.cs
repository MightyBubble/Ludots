using System.Text;
using System.Text.Json;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Registry;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using Ludots.WebUI.DataPlane;
using Ludots.WebUI.PanelKit;
using NUnit.Framework;

namespace Ludots.Tests.WebUiPanelKit;

[TestFixture]
public sealed class WebUiTooltipPanelTests
{
	[Test]
	public void LoadSampleDescriptor_ProjectsEntityInsightProfile_NotParallelTooltipProfile()
	{
		WebUiTooltipReferenceCatalog catalog = WebUiTooltipSampleCatalog.Create();
		WebUiTooltipDescriptor descriptor = WebUiTooltipDescriptorLoader.LoadFromFile(
			WebUiTooltipSampleCatalog.SampleDescriptorPath(),
			catalog);

		Assert.That(descriptor.DescriptorId, Is.EqualTo(WebUiTooltipSampleCatalog.DescriptorId));
		Assert.That(descriptor.TargetKind, Is.EqualTo(WebUiTooltipTargetKind.EntityInsight));
		Assert.That(descriptor.ProfileId, Is.EqualTo(WebUiTooltipSampleCatalog.EntityInsightProfileId));
		Assert.That(descriptor.Sections, Has.Count.EqualTo(2));
	}

	[Test]
	public void TopicProducer_Payload_IsStructuredRichText_NotHtml()
	{
		WebUiTooltipDescriptor descriptor = LoadSampleDescriptor();
		var producer = new WebUiTooltipTopicProducer(
			WebUiTooltipSampleCatalog.Topic,
			descriptor,
			tokenId => WebUiTooltipSampleCatalog.Create().IsTokenRegistered(tokenId),
			(tokenId, localeId) => WebUiTooltipSampleCatalog.Create().HasLocaleTemplate(tokenId, localeId),
			entityProjection: WebUiTooltipSampleCatalog.CreateSampleEntityProjection(),
			stateFlags: ["state.tooltip.visible"]);

		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(producer);
		Assert.That(runtime.IsTopicRegistered(WebUiTooltipSampleCatalog.Topic), Is.True);

		var context = new WebUiTopicContext("session-a", producer.Topic, 3, JsonSerializer.SerializeToElement(new { }));
		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		Assert.That(packet.ContentType, Is.EqualTo(WebUiTooltipTopicProducer.JsonContentType));

		string json = Encoding.UTF8.GetString(packet.Payload.Span);
		Assert.That(json, Does.Not.Contain("<"));
		Assert.That(json, Does.Not.Contain(">"));
		Assert.That(json, Does.Not.Contain("Unknown"));
		Assert.That(json, Does.Not.Contain("Ability#"));

		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		JsonElement root = document.RootElement;
		Assert.That(root.GetProperty("target").GetProperty("kind").GetString(), Is.EqualTo(nameof(WebUiTooltipTargetKind.EntityInsight)));
		Assert.That(root.GetProperty("target").GetProperty("id").GetString(), Is.EqualTo(WebUiTooltipSampleCatalog.EntityInsightProfileId));
		Assert.That(root.GetProperty("profileId").GetString(), Is.EqualTo(WebUiTooltipSampleCatalog.EntityInsightProfileId));
		Assert.That(root.GetProperty("templateId").GetString(), Is.EqualTo(WebUiTooltipSampleCatalog.TemplateId));
		Assert.That(root.GetProperty("localeId").GetString(), Is.EqualTo(WebUiTooltipSampleCatalog.LocaleId));
		Assert.That(root.GetProperty("revision").GetUInt32(), Is.GreaterThan(0u));
		Assert.That(root.GetProperty("anchor").GetString(), Is.EqualTo(WebUiTooltipSampleCatalog.AnchorId));
		Assert.That(root.GetProperty("stateFlags")[0].GetString(), Is.EqualTo("state.tooltip.visible"));

		JsonElement sections = root.GetProperty("sections");
		Assert.That(sections.GetArrayLength(), Is.EqualTo(2));
		JsonElement titleRun = sections[0].GetProperty("blocks")[0].GetProperty("runs")[0];
		Assert.That(titleRun.GetProperty("role").GetString(), Is.EqualTo("token"));
		Assert.That(titleRun.GetProperty("tokenId").GetString(), Is.EqualTo(WebUiTooltipSampleCatalog.TitleTokenId));
		Assert.That(titleRun.TryGetProperty("text", out _), Is.False);
	}

	[Test]
	public void Load_UnknownToken_FailsFastWithConcreteId()
	{
		WebUiTooltipReferenceCatalog catalog = WebUiTooltipSampleCatalog.Create();
		string json = """
		{
		  "descriptorId": "panel-kit.test.tooltip",
		  "targetKind": "entityInsight",
		  "profileId": "profile.insight.generic",
		  "templateId": "template.tooltip.generic",
		  "localeId": "locale.sample",
		  "anchor": "anchor.cursor",
		  "sections": [
		    {
		      "sectionId": "section.a",
		      "templateId": "template.tooltip.section.title",
		      "blocks": [
		        {
		          "blockId": "block.a",
		          "runs": [ { "role": "token", "tokenId": "token.missing.tooltip" } ]
		        }
		      ]
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTooltipDescriptorLoader.LoadFromJson(json, catalog, "token-test"))!;

		Assert.That(ex.Message, Does.Contain("token.missing.tooltip"));
		Assert.That(ex.Message, Does.Contain("unknown text token"));
	}

	[Test]
	public void Load_UnknownLocale_FailsFastWithConcreteId()
	{
		WebUiTooltipReferenceCatalog catalog = WebUiTooltipSampleCatalog.Create();
		string json = """
		{
		  "descriptorId": "panel-kit.test.tooltip",
		  "targetKind": "entityInsight",
		  "profileId": "profile.insight.generic",
		  "templateId": "template.tooltip.generic",
		  "localeId": "locale.missing",
		  "anchor": "anchor.cursor",
		  "sections": [
		    {
		      "sectionId": "section.a",
		      "templateId": "template.tooltip.section.title",
		      "blocks": [
		        {
		          "blockId": "block.a",
		          "runs": [ { "role": "token", "tokenId": "token.tooltip.title" } ]
		        }
		      ]
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTooltipDescriptorLoader.LoadFromJson(json, catalog, "locale-test"))!;

		Assert.That(ex.Message, Does.Contain("locale.missing"));
		Assert.That(ex.Message, Does.Contain("unknown locale"));
	}

	[Test]
	public void Load_UnknownProfile_FailsFastWithConcreteId()
	{
		WebUiTooltipReferenceCatalog catalog = WebUiTooltipSampleCatalog.Create();
		string json = """
		{
		  "descriptorId": "panel-kit.test.tooltip",
		  "targetKind": "entityInsight",
		  "profileId": "profile.missing.insight",
		  "templateId": "template.tooltip.generic",
		  "localeId": "locale.sample",
		  "anchor": "anchor.cursor",
		  "sections": [
		    {
		      "sectionId": "section.a",
		      "templateId": "template.tooltip.section.title",
		      "blocks": [
		        {
		          "blockId": "block.a",
		          "runs": [ { "role": "token", "tokenId": "token.tooltip.title" } ]
		        }
		      ]
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTooltipDescriptorLoader.LoadFromJson(json, catalog, "profile-test"))!;

		Assert.That(ex.Message, Does.Contain("profile.missing.insight"));
		Assert.That(ex.Message, Does.Contain("unknown profile"));
	}

	[Test]
	public void Load_UnknownTemplate_FailsFastWithConcreteId()
	{
		WebUiTooltipReferenceCatalog catalog = WebUiTooltipSampleCatalog.Create();
		string json = """
		{
		  "descriptorId": "panel-kit.test.tooltip",
		  "targetKind": "entityInsight",
		  "profileId": "profile.insight.generic",
		  "templateId": "template.missing.tooltip",
		  "localeId": "locale.sample",
		  "anchor": "anchor.cursor",
		  "sections": [
		    {
		      "sectionId": "section.a",
		      "templateId": "template.tooltip.section.title",
		      "blocks": [
		        {
		          "blockId": "block.a",
		          "runs": [ { "role": "token", "tokenId": "token.tooltip.title" } ]
		        }
		      ]
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTooltipDescriptorLoader.LoadFromJson(json, catalog, "template-test"))!;

		Assert.That(ex.Message, Does.Contain("template.missing.tooltip"));
		Assert.That(ex.Message, Does.Contain("unknown template"));
	}

	[Test]
	public void Load_UnknownRunRole_FailsFastWithConcreteId()
	{
		WebUiTooltipReferenceCatalog catalog = WebUiTooltipSampleCatalog.Create();
		string json = """
		{
		  "descriptorId": "panel-kit.test.tooltip",
		  "targetKind": "entityInsight",
		  "profileId": "profile.insight.generic",
		  "templateId": "template.tooltip.generic",
		  "localeId": "locale.sample",
		  "anchor": "anchor.cursor",
		  "sections": [
		    {
		      "sectionId": "section.a",
		      "templateId": "template.tooltip.section.title",
		      "blocks": [
		        {
		          "blockId": "block.a",
		          "runs": [ { "role": "htmlSnippet", "text": "bad" } ]
		        }
		      ]
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTooltipDescriptorLoader.LoadFromJson(json, catalog, "role-test"))!;

		Assert.That(ex.Message, Does.Contain("htmlSnippet"));
		Assert.That(ex.Message, Does.Contain("unknown rich-text run role"));
	}

	[Test]
	public void Load_HtmlInTextRun_FailsFast()
	{
		WebUiTooltipReferenceCatalog catalog = WebUiTooltipSampleCatalog.Create();
		string json = """
		{
		  "descriptorId": "panel-kit.test.tooltip",
		  "targetKind": "entityInsight",
		  "profileId": "profile.insight.generic",
		  "templateId": "template.tooltip.generic",
		  "localeId": "locale.sample",
		  "anchor": "anchor.cursor",
		  "sections": [
		    {
		      "sectionId": "section.a",
		      "templateId": "template.tooltip.section.title",
		      "blocks": [
		        {
		          "blockId": "block.a",
		          "runs": [ { "role": "text", "text": "<b>bad</b>" } ]
		        }
		      ]
		    }
		  ]
		}
		""";

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTooltipDescriptorLoader.LoadFromJson(json, catalog, "html-test"))!;

		Assert.That(ex.Message, Does.Contain("HTML"));
	}

	[Test]
	public void EntityInsight_MissingProfile_FailsFast_DoesNotInventTooltipProfile()
	{
		WebUiTooltipReferenceCatalog catalog = WebUiTooltipSampleCatalog.Create(
			isEntityInsightProfileRegistered: _ => false);

		string json = File.ReadAllText(WebUiTooltipSampleCatalog.SampleDescriptorPath());
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			WebUiTooltipDescriptorLoader.LoadFromJson(json, catalog, "insight-boundary"))!;

		Assert.That(ex.Message, Does.Contain(WebUiTooltipSampleCatalog.EntityInsightProfileId));
		Assert.That(ex.Message, Does.Contain("EntityInsightProfile"));
	}

	[Test]
	public void AbilityPresentationTextValidator_MissingToken_FailsFast()
	{
		PresentationTextCatalog textCatalog = CreateTextCatalog(
			("token.ability.sample.name", "Sample Ability"),
			("token.ability.sample.hint", "Sample hint"));

		var presentation = new AbilityPresentationConfig
		{
			DisplayNameToken = "token.ability.missing",
			HintTextToken = "token.ability.sample.hint"
		};

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			AbilityPresentationTextValidator.Validate("Ability.Sample", presentation, textCatalog, "en-US"))!;

		Assert.That(ex.Message, Does.Contain("token.ability.missing"));
		Assert.That(ex.Message, Does.Contain("unknown text token"));
		Assert.That(ex.Message, Does.Not.Contain("Unknown"));
		Assert.That(ex.Message, Does.Not.Contain("Ability#"));
	}

	[Test]
	public void AbilityPresentationTextValidator_MissingLocaleCoverage_FailsFast()
	{
		PresentationTextCatalog textCatalog = CreateTextCatalog(
			("token.ability.sample.name", "Sample Ability"));

		var presentation = new AbilityPresentationConfig
		{
			DisplayNameToken = "token.ability.sample.name",
			HintTextToken = "token.ability.sample.name"
		};

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			AbilityPresentationTextValidator.Validate("Ability.Sample", presentation, textCatalog, "zh-CN"))!;

		Assert.That(ex.Message, Does.Contain("zh-CN"));
		Assert.That(ex.Message, Does.Contain("unknown locale").Or.Contain("no template"));
	}

	[Test]
	public void AbilityPresentationTextValidator_FinalStringsWithoutTokens_RejectedForTokenPath()
	{
		PresentationTextCatalog textCatalog = CreateTextCatalog(("token.ability.sample.name", "Sample Ability"));
		var presentation = new AbilityPresentationConfig
		{
			DisplayName = "Hardcoded English Name",
			HintText = "Hardcoded English Hint"
		};

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			AbilityPresentationTextValidator.Validate("Ability.Sample", presentation, textCatalog, "en-US"))!;

		Assert.That(ex.Message, Does.Contain("displayNameToken"));
		Assert.That(ex.Message, Does.Contain("migration debt"));
	}

	[Test]
	public void AbilityExecLoader_CompilesPresentationTokens()
	{
		var obj = System.Text.Json.Nodes.JsonNode.Parse(
			"""
			{
			  "exec": {
			    "clockId": "FixedFrame",
			    "items": [ { "kind": "End", "tick": 0 } ]
			  },
			  "presentation": {
			    "displayNameToken": "token.ability.sample.name",
			    "hintTextToken": "token.ability.sample.hint",
			    "modeHintTokens": {
			      "SmartCast": "token.ability.sample.mode.smart"
			    }
			  }
			}
			""")!.AsObject();

		AbilityDefinition def = AbilityExecLoader.CompileAbility(obj, "Ability.Sample.Tokenized", "GAS/abilities.json");

		Assert.That(def.HasPresentation, Is.True);
		Assert.That(def.Presentation!.DisplayNameToken, Is.EqualTo("token.ability.sample.name"));
		Assert.That(def.Presentation.HintTextToken, Is.EqualTo("token.ability.sample.hint"));
		Assert.That(def.Presentation.ModeHintTokenOverrides["SmartCast"], Is.EqualTo("token.ability.sample.mode.smart"));
		Assert.That(def.Presentation.HasPresentationTokens, Is.True);
	}

	[Test]
	public void SampleDescriptor_ContainsNoHardcodedGameNames()
	{
		string json = File.ReadAllText(WebUiTooltipSampleCatalog.SampleDescriptorPath());
		string[] forbidden =
		[
			"CK3", "Stellaris", "群星", "Command", "Conquer", "Age of Empires", "StarCraft",
			"Minerals", "Vespene", "Marine", "Infantry", "Unknown", "Ability#"
		];

		foreach (string token in forbidden)
		{
			Assert.That(json, Does.Not.Contain(token), $"Sample tooltip descriptor must not hardcode '{token}'.");
		}
	}

	[Test]
	public void BrowserSubscriptionTopics_StillComeFromWpkManifest_WhenTooltipTopicRegistered()
	{
		WebUiTooltipDescriptor descriptor = LoadSampleDescriptor();
		var tooltipProducer = new WebUiTooltipTopicProducer(
			WebUiTooltipSampleCatalog.Topic,
			descriptor,
			tokenId => WebUiTooltipSampleCatalog.Create().IsTokenRegistered(tokenId),
			(tokenId, localeId) => WebUiTooltipSampleCatalog.Create().HasLocaleTemplate(tokenId, localeId),
			entityProjection: WebUiTooltipSampleCatalog.CreateSampleEntityProjection());

		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(new StubTopicProducer(WebUiPanelKitSampleCatalog.ResourceTopic));
		runtime.RegisterTopic(new StubTopicProducer(WebUiPanelKitSampleCatalog.CommandTopic));
		runtime.RegisterTopic(new StubTopicProducer(WebUiPanelKitSampleCatalog.ObjectiveTopic));
		runtime.RegisterTopic(tooltipProducer);
		runtime.RegisterTopic(new StubTopicProducer("panel-kit.extra.unrelated"));

		WebUiPanelKitReferenceCatalog panelCatalog = WebUiPanelKitSampleCatalog.Create(runtime.IsTopicRegistered);
		WebUiPanelKitManifest manifest = WebUiPanelKitManifestLoader.LoadFromFile(
			WebUiPanelKitSampleCatalog.SampleManifestPath(),
			panelCatalog);

		CreateRoot(out UiSurfaceHost host);
		using var binder = new WebUiPanelKitSurfaceBinder(host, manifest);
		binder.Bind();

		Assert.That(binder.BrowserSubscriptionTopics, Is.EqualTo(new[]
		{
			WebUiPanelKitSampleCatalog.ResourceTopic,
			WebUiPanelKitSampleCatalog.CommandTopic,
			WebUiPanelKitSampleCatalog.ObjectiveTopic
		}));
		Assert.That(binder.BrowserSubscriptionTopics, Does.Not.Contain(WebUiTooltipSampleCatalog.Topic));
		Assert.That(runtime.IsTopicRegistered(WebUiTooltipSampleCatalog.Topic), Is.True);
	}

	private static WebUiTooltipDescriptor LoadSampleDescriptor()
	{
		return WebUiTooltipDescriptorLoader.LoadFromFile(
			WebUiTooltipSampleCatalog.SampleDescriptorPath(),
			WebUiTooltipSampleCatalog.Create());
	}

	private static PresentationTextCatalog CreateTextCatalog(params (string Token, string Template)[] entries)
	{
		var tokenIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
		var tokens = new PresentationTextTokenDefinition[entries.Length + 2];
		var templates = new PresentationTextTemplate[entries.Length + 2];
		for (int i = 0; i < entries.Length; i++)
		{
			int id = tokenIds.Register(entries[i].Token);
			tokens[id] = new PresentationTextTokenDefinition
			{
				TokenId = id,
				Key = entries[i].Token,
				ArgCount = 0
			};
			templates[id] = new PresentationTextTemplate(
				entries[i].Template,
				[new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Literal, entries[i].Template, argIndex: -1)]);
		}

		tokenIds.Freeze();
		var localeIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
		int localeId = localeIds.Register("en-US");
		localeIds.Freeze();
		var locales = new PresentationTextLocaleTable[localeId + 1];
		locales[localeId] = new PresentationTextLocaleTable(localeId, "en-US", templates);
		return new PresentationTextCatalog(tokenIds, tokens, localeIds, locales, defaultLocaleId: localeId);
	}

	private static UIRoot CreateRoot(out UiSurfaceHost host)
	{
		var root = new UIRoot(new NullUiRenderer());
		host = new UiSurfaceHost(root, new SkiaTextMeasurer(), new SkiaImageSizeProvider());
		return root;
	}

	private sealed class StubTopicProducer : IWebUiTopicProducer
	{
		public StubTopicProducer(string topic)
		{
			Topic = topic;
		}

		public string Topic { get; }

		public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
		{
			packet = new WebUiOutboundPacket(
				context.SessionId,
				Topic,
				WebUiPacketKind.Snapshot,
				WebUiDeliverySemantics.LatestWins,
				Encoding.UTF8.GetBytes("{}"),
				"application/json",
				context.RequestId);
			return true;
		}
	}

	private sealed class NullUiRenderer : IUiRenderer
	{
		public void Render(UiScene scene, float width, float height)
		{
		}
	}
}
