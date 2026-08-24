using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.WebUI.DataPlane;

/// <summary>
/// DataPlane topic producer for the Notification panel.
/// SSOT is <see cref="NotificationRuntime"/> only — not Task, NarrativeFrontend, or showcase toast state.
/// Web clients render the ordered snapshot; they do not reconstruct event history.
/// </summary>
public sealed class NotificationWebUiTopicProducer : IWebUiTopicProducer
{
	public const string JsonContentType = "application/json+ludots-notification";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};

	private readonly NotificationRuntime _runtime;
	private readonly NotificationPanelProfile _profile;

	public NotificationWebUiTopicProducer(
		string topic,
		NotificationRuntime runtime,
		NotificationPanelProfile profile)
	{
		Topic = string.IsNullOrWhiteSpace(topic)
			? throw new ArgumentException("Topic is required.", nameof(topic))
			: topic.Trim();
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_profile = profile ?? throw new ArgumentNullException(nameof(profile));
	}

	public string Topic { get; }

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		NotificationWebSnapshot snapshot = BuildSnapshot();
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
		packet = new WebUiOutboundPacket(
			context.SessionId,
			Topic,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			JsonContentType,
			context.RequestId);
		return true;
	}

	public NotificationWebSnapshot BuildSnapshot()
	{
		IReadOnlyList<NotificationMessage> active = _runtime.SnapshotActive(_profile);
		var rows = new NotificationWebRow[active.Count];
		for (int i = 0; i < active.Count; i++)
		{
			NotificationMessage message = active[i];
			var actions = new NotificationWebAction[message.Actions.Count];
			for (int a = 0; a < message.Actions.Count; a++)
			{
				NotificationAction action = message.Actions[a];
				string commandName = _runtime.ActionRegistry.RequireCommandName(action.ActionId);
				actions[a] = new NotificationWebAction(
					action.ActionId,
					commandName,
					action.LabelTokenId,
					action.Payload.ValueKind == JsonValueKind.Undefined ? null : action.Payload);
			}

			rows[i] = new NotificationWebRow(
				message.Id,
				message.CategoryId,
				message.Severity,
				message.TextTokenId,
				message.DedupeKey,
				message.Priority,
				message.TtlSeconds,
				message.CreatedAtSeconds,
				actions);
		}

		return new NotificationWebSnapshot(
			_profile.ProfileId,
			_profile.PanelKind,
			_profile.LocaleId,
			_runtime.Revision,
			rows);
	}
}

/// <summary>
/// Ordered notification snapshot for Web rendering. Revision changes when the active set changes.
/// </summary>
public sealed record NotificationWebSnapshot(
	string ProfileId,
	NotificationPanelKind PanelKind,
	string LocaleId,
	uint Revision,
	NotificationWebRow[] Notifications);

public sealed record NotificationWebRow(
	string Id,
	string CategoryId,
	NotificationSeverity Severity,
	string TextTokenId,
	string DedupeKey,
	int Priority,
	double? TtlSeconds,
	double CreatedAtSeconds,
	NotificationWebAction[] Actions);

public sealed record NotificationWebAction(
	string ActionId,
	string CommandName,
	string? LabelTokenId,
	JsonElement? Payload);
