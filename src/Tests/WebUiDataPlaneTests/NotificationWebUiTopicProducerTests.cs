using System.Text;
using System.Text.Json;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class NotificationWebUiTopicProducerTests
{
	private const string Topic = "panel-kit.sample.notification";
	private const string ProfileId = "profile.notification.generic";
	private const string LocaleId = NotificationPanelProfile.DefaultLocaleId;
	private const string TechCompleteToken = "token.notification.tech.complete";
	private const string ActionLabelToken = "token.notification.action.open-panel";
	private const string OpenPanelActionId = "action.notification.open-panel";
	private const string OpenPanelCommandName = "notification.openPanel";
	private const string CategoryResearch = "category.research";
	private const string CategoryCombat = "category.combat";

	[Test]
	public void Publish_ProjectsOrderedSnapshot_WithRevisionAndTextToken()
	{
		(NotificationRuntime runtime, NotificationActionRegistry actions, HashSet<string> _) = CreateRuntime();
		actions.Register(OpenPanelActionId, OpenPanelCommandName);

		runtime.Publish(CreateMessage(
			"note.tech.a",
			CategoryResearch,
			NotificationSeverity.Info,
			TechCompleteToken,
			"dedupe.tech.a",
			priority: 10,
			createdAt: 1d,
			actions: [new NotificationAction(OpenPanelActionId, ActionLabelToken)]));

		runtime.Publish(CreateMessage(
			"note.combat.b",
			CategoryCombat,
			NotificationSeverity.Critical,
			TechCompleteToken,
			"dedupe.combat.b",
			priority: 5,
			createdAt: 2d));

		var producer = new NotificationWebUiTopicProducer(
			Topic,
			runtime,
			NotificationPanelProfile.CreateGeneric());

		using var dataPlane = new WebUiDataPlaneRuntime();
		dataPlane.RegisterTopic(producer);
		Assert.That(dataPlane.IsTopicRegistered(Topic), Is.True);

		NotificationWebSnapshot snapshot = producer.BuildSnapshot();
		Assert.That(snapshot.ProfileId, Is.EqualTo(ProfileId));
		Assert.That(snapshot.PanelKind, Is.EqualTo(NotificationPanelKind.ToastStack));
		Assert.That(snapshot.LocaleId, Is.EqualTo(LocaleId));
		Assert.That(snapshot.Revision, Is.GreaterThan(0u));
		Assert.That(snapshot.Notifications, Has.Length.EqualTo(2));
		// Higher priority first, then severity, then createdAt, then id.
		Assert.That(snapshot.Notifications[0].Id, Is.EqualTo("note.tech.a"));
		Assert.That(snapshot.Notifications[0].TextTokenId, Is.EqualTo(TechCompleteToken));
		Assert.That(snapshot.Notifications[0].Actions, Has.Length.EqualTo(1));
		Assert.That(snapshot.Notifications[0].Actions[0].ActionId, Is.EqualTo(OpenPanelActionId));
		Assert.That(snapshot.Notifications[0].Actions[0].CommandName, Is.EqualTo(OpenPanelCommandName));
		Assert.That(snapshot.Notifications[1].Id, Is.EqualTo("note.combat.b"));

		var context = new WebUiTopicContext("session-a", producer.Topic, 1, JsonSerializer.SerializeToElement(new { }));
		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		Assert.That(packet.ContentType, Is.EqualTo(NotificationWebUiTopicProducer.JsonContentType));
		string json = Encoding.UTF8.GetString(packet.Payload.Span);
		Assert.That(json, Does.Contain(TechCompleteToken));
		Assert.That(json, Does.Not.Contain("NarrativeFrontend"));
		Assert.That(json, Does.Not.Contain("TaskRuntime"));
		Assert.That(json, Does.Not.Contain("Unknown"));
	}

	[Test]
	public void DedupeKey_ReplacesPriorMessage_AndBumpsRevision()
	{
		(NotificationRuntime runtime, NotificationActionRegistry _, HashSet<string> _) = CreateRuntime();
		runtime.Publish(CreateMessage("note.1", CategoryResearch, NotificationSeverity.Info, TechCompleteToken, "dedupe.same", 1, 1d));
		uint revision1 = runtime.Revision;
		NotificationWebSnapshot before = new NotificationWebUiTopicProducer(
			"topic.notification",
			runtime,
			NotificationPanelProfile.CreateGeneric()).BuildSnapshot();
		Assert.That(before.Notifications, Has.Length.EqualTo(1));
		Assert.That(before.Notifications[0].Id, Is.EqualTo("note.1"));

		runtime.Publish(CreateMessage("note.2", CategoryResearch, NotificationSeverity.Warning, TechCompleteToken, "dedupe.same", 2, 2d));
		Assert.That(runtime.Revision, Is.GreaterThan(revision1));
		Assert.That(runtime.TryGet("note.1", out _), Is.False);

		NotificationWebSnapshot after = new NotificationWebUiTopicProducer(
			"topic.notification",
			runtime,
			NotificationPanelProfile.CreateGeneric()).BuildSnapshot();
		Assert.That(after.Notifications, Has.Length.EqualTo(1));
		Assert.That(after.Notifications[0].Id, Is.EqualTo("note.2"));
		Assert.That(after.Notifications[0].Severity, Is.EqualTo(NotificationSeverity.Warning));
		Assert.That(after.Revision, Is.Not.EqualTo(before.Revision));
	}

	[Test]
	public void TtlExpiry_RemovesMessage_FromSnapshot()
	{
		double now = 0d;
		(NotificationRuntime runtime, NotificationActionRegistry _, HashSet<string> _) = CreateRuntime(clock: () => now);
		runtime.Publish(CreateMessage(
			"note.ttl",
			CategoryResearch,
			NotificationSeverity.Info,
			TechCompleteToken,
			"dedupe.ttl",
			1,
			createdAt: 0d,
			ttlSeconds: 5d));

		Assert.That(new NotificationWebUiTopicProducer("t", runtime, NotificationPanelProfile.CreateGeneric())
			.BuildSnapshot().Notifications, Has.Length.EqualTo(1));

		now = 6d;
		NotificationWebSnapshot expired = new NotificationWebUiTopicProducer(
			"t",
			runtime,
			NotificationPanelProfile.CreateGeneric()).BuildSnapshot();
		Assert.That(expired.Notifications, Is.Empty);
	}

	[Test]
	public void MissingTextToken_FailsFastWithConcreteId()
	{
		(NotificationRuntime runtime, NotificationActionRegistry _, HashSet<string> _) = CreateRuntime();
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			runtime.Publish(CreateMessage(
				"note.missing",
				CategoryResearch,
				NotificationSeverity.Info,
				"token.notification.missing",
				"dedupe.missing",
				1,
				1d)))!;

		Assert.That(ex.Message, Does.Contain("token.notification.missing"));
		Assert.That(ex.Message, Does.Contain("unknown text token"));
	}

	[Test]
	public void MissingLocaleCoverage_FailsFastWithConcreteIds()
	{
		var tokens = new HashSet<string>(StringComparer.Ordinal) { TechCompleteToken };
		var actions = new NotificationActionRegistry();
		var validator = new NotificationTextValidator(
			tokens.Contains,
			(token, locale) => tokens.Contains(token) && string.Equals(locale, "locale.other", StringComparison.Ordinal));
		var runtime = new NotificationRuntime(validator, actions, localeId: LocaleId);

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			runtime.Publish(CreateMessage(
				"note.locale",
				CategoryResearch,
				NotificationSeverity.Info,
				TechCompleteToken,
				"dedupe.locale",
				1,
				1d)))!;

		Assert.That(ex.Message, Does.Contain(TechCompleteToken));
		Assert.That(ex.Message, Does.Contain(LocaleId));
		Assert.That(ex.Message, Does.Contain("no template for locale"));
	}

	[Test]
	public void UnknownAction_FailsFastWithConcreteId()
	{
		(NotificationRuntime runtime, NotificationActionRegistry _, HashSet<string> _) = CreateRuntime();
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			runtime.Publish(CreateMessage(
				"note.action",
				CategoryResearch,
				NotificationSeverity.Info,
				TechCompleteToken,
				"dedupe.action",
				1,
				1d,
				actions: [new NotificationAction("action.notification.unknown")])))!;

		Assert.That(ex.Message, Does.Contain("action.notification.unknown"));
		Assert.That(ex.Message, Does.Contain("Unknown notification action"));
	}

	[Test]
	public void ResolveActionCommand_ReturnsRegisteredWebUiCommand()
	{
		var router = new WebUiCommandRouter(new AlwaysCurrentEntities(), new AllowAllPermissions());
		router.Register(OpenPanelCommandName, new RecordingCommandHandler());
		var tokens = new HashSet<string>(StringComparer.Ordinal) { TechCompleteToken, ActionLabelToken };
		var actions = new NotificationActionRegistry(router.IsRegistered);
		actions.Register(OpenPanelActionId, OpenPanelCommandName);
		var validator = new NotificationTextValidator(
			tokens.Contains,
			(token, locale) => tokens.Contains(token) && string.Equals(locale, LocaleId, StringComparison.Ordinal));
		var runtime = new NotificationRuntime(validator, actions, localeId: LocaleId);

		runtime.Publish(CreateMessage(
			"note.tech",
			CategoryResearch,
			NotificationSeverity.Info,
			TechCompleteToken,
			"dedupe.tech",
			1,
			1d,
			actions: [new NotificationAction(OpenPanelActionId, ActionLabelToken)]));

		string command = runtime.ResolveActionCommand("note.tech", OpenPanelActionId);
		Assert.That(command, Is.EqualTo(OpenPanelCommandName));
		Assert.That(router.IsRegistered(command), Is.True);
	}

	[Test]
	public void ActionRegistry_UnknownCommand_FailsFastWithConcreteId()
	{
		var router = new WebUiCommandRouter(new AlwaysCurrentEntities(), new AllowAllPermissions());
		var actions = new NotificationActionRegistry(router.IsRegistered);
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			actions.Register(OpenPanelActionId, OpenPanelCommandName))!;
		Assert.That(ex.Message, Does.Contain(OpenPanelCommandName));
		Assert.That(ex.Message, Does.Contain("unknown WebUI command"));
	}

	[Test]
	public void ProfileFilters_ByCategoryAndMaxVisible()
	{
		(NotificationRuntime runtime, NotificationActionRegistry _, HashSet<string> _) = CreateRuntime();
		runtime.Publish(CreateMessage("n1", CategoryResearch, NotificationSeverity.Info, TechCompleteToken, "d1", 3, 1d));
		runtime.Publish(CreateMessage("n2", CategoryCombat, NotificationSeverity.Warning, TechCompleteToken, "d2", 2, 2d));
		runtime.Publish(CreateMessage("n3", CategoryResearch, NotificationSeverity.Critical, TechCompleteToken, "d3", 1, 3d));

		var profile = new NotificationPanelProfile(
			ProfileId,
			NotificationPanelKind.EventFeed,
			[NotificationSeverity.Info, NotificationSeverity.Warning, NotificationSeverity.Critical],
			LocaleId,
			maxVisible: 1,
			allowedCategoryIds: [CategoryResearch]);

		NotificationWebSnapshot snapshot = new NotificationWebUiTopicProducer("t", runtime, profile).BuildSnapshot();
		Assert.That(snapshot.Notifications, Has.Length.EqualTo(1));
		Assert.That(snapshot.Notifications[0].Id, Is.EqualTo("n1"));
		Assert.That(snapshot.PanelKind, Is.EqualTo(NotificationPanelKind.EventFeed));
	}

	[Test]
	public void Runtime_DoesNotDependOnNarrativeOrShowcaseAssemblies()
	{
		System.Reflection.Assembly assembly = typeof(NotificationRuntime).Assembly;
		Assert.That(assembly.GetName().Name, Is.EqualTo("Ludots.WebUI.DataPlane"));
		string[] referenced = assembly.GetReferencedAssemblies().Select(static a => a.Name ?? string.Empty).ToArray();
		Assert.That(referenced, Does.Not.Contain("NarrativeFrontendMod"));
		Assert.That(referenced, Does.Not.Contain("RelationshipShowcaseMod"));
		Assert.That(typeof(NotificationRuntime).GetMethods().Select(static m => m.Name), Does.Contain("Publish"));
		Assert.That(typeof(NotificationRuntime).GetMethods().Select(static m => m.Name), Does.Contain("ResolveActionCommand"));
	}

	private static (NotificationRuntime Runtime, NotificationActionRegistry Actions, HashSet<string> Tokens) CreateRuntime(
		Func<double>? clock = null)
	{
		var tokens = new HashSet<string>(StringComparer.Ordinal)
		{
			TechCompleteToken,
			ActionLabelToken
		};
		var actions = new NotificationActionRegistry();
		var validator = new NotificationTextValidator(
			tokens.Contains,
			(token, locale) => tokens.Contains(token) && string.Equals(locale, LocaleId, StringComparison.Ordinal));
		var runtime = new NotificationRuntime(validator, actions, clock, LocaleId);
		return (runtime, actions, tokens);
	}

	private static NotificationMessage CreateMessage(
		string id,
		string category,
		NotificationSeverity severity,
		string textToken,
		string dedupeKey,
		int priority,
		double createdAt,
		double? ttlSeconds = null,
		IReadOnlyList<NotificationAction>? actions = null)
	{
		return new NotificationMessage(
			id,
			category,
			severity,
			textToken,
			dedupeKey,
			priority,
			ttlSeconds,
			actions,
			createdAt);
	}

	private sealed class AlwaysCurrentEntities : IWebUiEntityGenerationResolver
	{
		public bool IsCurrent(WebUiEntityRef entityRef) => true;
	}

	private sealed class AllowAllPermissions : IWebUiCommandPermissionValidator
	{
		public bool CanUse(WebUiCommandRequest request, out string error)
		{
			error = string.Empty;
			return true;
		}
	}

	private sealed class RecordingCommandHandler : IWebUiCommandHandler
	{
		public ValueTask<WebUiCommandResult> HandleAsync(
			WebUiCommandRequest request,
			CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(WebUiCommandResult.Ok());
		}
	}
}
