using System.Text.Json;
using Ludots.Core.UI.ProductionOverview;

namespace Ludots.WebUI.DataPlane;

/// <summary>
/// DataPlane topic producer for one production/worker/queue overview profile (WPK-4).
/// Web clients render owner/profile/revision/rows/queue/workers only — they do not own
/// a production store or invent worker state.
/// </summary>
public sealed class ProductionOverviewWebUiTopicProducer : IWebUiTopicProducer
{
	public const string JsonContentType = "application/json+ludots-production-overview";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly ProductionOverviewProjector _projector;
	private readonly ProductionOverviewProfile _profile;
	private readonly Func<ProductionOverviewBindingContext> _bindingFactory;

	public ProductionOverviewWebUiTopicProducer(
		string topic,
		ProductionOverviewProjector projector,
		ProductionOverviewProfile profile,
		Func<ProductionOverviewBindingContext> bindingFactory)
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
				$"Production overview profile '{profile.Id}' topic '{profile.Topic}' does not match producer topic '{Topic}'.");
		}
	}

	public string Topic { get; }

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		ProductionOverviewBindingContext binding = _bindingFactory();
		ProductionOverviewSnapshot snapshot = _projector.Project(_profile, in binding);
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(ToDto(snapshot), JsonOptions);
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

	private static ProductionOverviewWebSnapshot ToDto(ProductionOverviewSnapshot snapshot)
	{
		var rows = new ProductionOverviewWebStatusRow[snapshot.Rows.Count];
		for (int i = 0; i < snapshot.Rows.Count; i++)
		{
			ProductionOverviewStatusRow row = snapshot.Rows[i];
			rows[i] = new ProductionOverviewWebStatusRow(
				row.OwnerEntityId,
				row.OwnerVersion,
				row.Label,
				row.Detail,
				row.ProgressPermille,
				row.AccentColorHex,
				row.BlockedReason);
		}

		var queueItems = new ProductionOverviewWebQueueItem[snapshot.QueueItems.Count];
		for (int i = 0; i < snapshot.QueueItems.Count; i++)
		{
			ProductionOverviewQueueItem item = snapshot.QueueItems[i];
			queueItems[i] = new ProductionOverviewWebQueueItem(
				item.OwnerEntityId,
				item.OwnerVersion,
				item.Stage,
				item.Label,
				item.Detail,
				item.ProgressPermille,
				item.AccentColorHex,
				item.BlockedReason);
		}

		var workerRows = new ProductionOverviewWebWorkerRow[snapshot.WorkerRows.Count];
		for (int i = 0; i < snapshot.WorkerRows.Count; i++)
		{
			ProductionOverviewWorkerRow row = snapshot.WorkerRows[i];
			workerRows[i] = new ProductionOverviewWebWorkerRow(
				row.BucketId,
				row.DisplayTokenId,
				row.Count,
				row.SortOrder);
		}

		return new ProductionOverviewWebSnapshot(
			snapshot.ProfileId,
			snapshot.OwnerEntityId,
			snapshot.OwnerVersion,
			snapshot.Revision,
			rows,
			queueItems,
			workerRows,
			snapshot.BlockedReasons.ToArray());
	}

	public sealed record ProductionOverviewWebSnapshot(
		string ProfileId,
		int OwnerEntityId,
		int OwnerVersion,
		uint Revision,
		ProductionOverviewWebStatusRow[] Rows,
		ProductionOverviewWebQueueItem[] QueueItems,
		ProductionOverviewWebWorkerRow[] WorkerRows,
		string[] BlockedReasons);

	public sealed record ProductionOverviewWebStatusRow(
		int OwnerEntityId,
		int OwnerVersion,
		string Label,
		string Detail,
		short ProgressPermille,
		string AccentColorHex,
		string BlockedReason);

	public sealed record ProductionOverviewWebQueueItem(
		int OwnerEntityId,
		int OwnerVersion,
		string Stage,
		string Label,
		string Detail,
		short ProgressPermille,
		string AccentColorHex,
		string BlockedReason);

	public sealed record ProductionOverviewWebWorkerRow(
		string BucketId,
		string DisplayTokenId,
		int Count,
		int SortOrder);
}
