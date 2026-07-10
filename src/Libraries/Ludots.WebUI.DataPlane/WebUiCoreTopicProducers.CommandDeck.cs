using System.Text.Json;
using Ludots.Core.UI.CommandDeck;

namespace Ludots.WebUI.DataPlane;

/// <summary>
/// DataPlane topic producer for one CommandDeck profile. Web clients render the payload only —
/// they do not scan entities, guess commands, or maintain availability state.
/// </summary>
public sealed class CommandDeckWebUiTopicProducer : IWebUiTopicProducer
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly CommandDeckProjector _projector;
	private readonly CommandDeckProfile _profile;
	private readonly Func<CommandDeckBindingContext> _bindingFactory;

	public CommandDeckWebUiTopicProducer(
		string topic,
		CommandDeckProjector projector,
		CommandDeckProfile profile,
		Func<CommandDeckBindingContext> bindingFactory)
	{
		Topic = string.IsNullOrWhiteSpace(topic)
			? throw new ArgumentException("Topic is required.", nameof(topic))
			: topic.Trim();
		_projector = projector ?? throw new ArgumentNullException(nameof(projector));
		_profile = profile ?? throw new ArgumentNullException(nameof(profile));
		_bindingFactory = bindingFactory ?? throw new ArgumentNullException(nameof(bindingFactory));

		if (!string.IsNullOrWhiteSpace(profile.Topic) &&
		    !string.Equals(profile.Topic, Topic, StringComparison.Ordinal))
		{
			throw new InvalidOperationException(
				$"CommandDeck profile '{profile.Id}' topic '{profile.Topic}' does not match producer topic '{Topic}'.");
		}
	}

	public string Topic { get; }

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		CommandDeckBindingContext binding = _bindingFactory();
		CommandDeckSnapshot snapshot = _projector.Project(_profile, in binding);
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(ToDto(snapshot), JsonOptions);
		packet = new WebUiOutboundPacket(
			context.SessionId,
			Topic,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			"application/json+ludots-command-deck",
			context.RequestId);
		return true;
	}

	private static CommandDeckWebSnapshot ToDto(CommandDeckSnapshot snapshot)
	{
		var entries = new CommandDeckWebEntry[snapshot.Entries.Count];
		for (int i = 0; i < snapshot.Entries.Count; i++)
		{
			CommandDeckEntry entry = snapshot.Entries[i];
			entries[i] = new CommandDeckWebEntry(
				entry.SlotIndex,
				entry.AbilityId,
				entry.ActionId,
				entry.DisplayLabel,
				entry.CategoryId,
				entry.Status,
				entry.BlockedReason,
				entry.OwnerCount,
				entry.RouteProfileId,
				entry.RoutedOwnerEntityId,
				entry.RoutedOwnerVersion,
				entry.RoutedSlotIndex,
				entry.CooldownPermille,
				entry.ChargesCurrent,
				entry.ChargesMax);
		}

		return new CommandDeckWebSnapshot(
			snapshot.ProfileId,
			ToModeId(snapshot.DisplayMode),
			snapshot.Revision,
			snapshot.Visible,
			entries);
	}

	private static string ToModeId(CommandDeckDisplayMode mode)
	{
		return mode switch
		{
			CommandDeckDisplayMode.Global => CommandDeckDisplayModeIds.Global,
			CommandDeckDisplayMode.Entity => CommandDeckDisplayModeIds.Entity,
			CommandDeckDisplayMode.AggregateFiltered => CommandDeckDisplayModeIds.AggregateFiltered,
			CommandDeckDisplayMode.ConditionalPinned => CommandDeckDisplayModeIds.ConditionalPinned,
			_ => throw new InvalidOperationException($"Unknown CommandDeck display mode '{mode}'.")
		};
	}

	public sealed record CommandDeckWebSnapshot(
		string ProfileId,
		string DisplayMode,
		uint Revision,
		bool Visible,
		CommandDeckWebEntry[] Entries);

	public sealed record CommandDeckWebEntry(
		int SlotIndex,
		int AbilityId,
		string ActionId,
		string DisplayLabel,
		string CategoryId,
		string Status,
		string BlockedReason,
		int OwnerCount,
		string RouteProfileId,
		int RoutedOwnerEntityId,
		int RoutedOwnerVersion,
		int RoutedSlotIndex,
		short CooldownPermille,
		short ChargesCurrent,
		short ChargesMax);
}
