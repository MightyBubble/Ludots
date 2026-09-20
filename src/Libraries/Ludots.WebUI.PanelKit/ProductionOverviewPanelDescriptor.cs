namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Production/worker/queue overview panel kit descriptor (WPK-4). Extends the generic panel
/// declaration with source/queue/worker profile references. Does not carry gameplay truth —
/// only stable ids over existing command/status/queue and entity collections.
/// </summary>
public sealed class ProductionOverviewPanelDescriptor
{
	public ProductionOverviewPanelDescriptor(
		string panelId,
		string sourceKind,
		string sourceRef,
		string commandPanelSourceId,
		string queueSourceKind,
		string topic,
		string profileId,
		string layoutId,
		string workerCollectionKey = "",
		IReadOnlyList<ProductionOverviewWorkerBucketRef>? workerBuckets = null)
	{
		PanelId = RequireId(panelId, nameof(panelId));
		SourceKind = RequireId(sourceKind, nameof(sourceKind));
		SourceRef = sourceRef?.Trim() ?? string.Empty;
		CommandPanelSourceId = RequireId(commandPanelSourceId, nameof(commandPanelSourceId));
		QueueSourceKind = RequireId(queueSourceKind, nameof(queueSourceKind));
		Topic = RequireId(topic, nameof(topic));
		ProfileId = RequireId(profileId, nameof(profileId));
		LayoutId = RequireId(layoutId, nameof(layoutId));
		WorkerCollectionKey = workerCollectionKey?.Trim() ?? string.Empty;
		WorkerBuckets = workerBuckets ?? Array.Empty<ProductionOverviewWorkerBucketRef>();

		Validate();
	}

	public string PanelId { get; }
	public string SourceKind { get; }
	public string SourceRef { get; }
	public string CommandPanelSourceId { get; }
	public string QueueSourceKind { get; }
	public string Topic { get; }
	public string ProfileId { get; }
	public string LayoutId { get; }
	public string WorkerCollectionKey { get; }
	public IReadOnlyList<ProductionOverviewWorkerBucketRef> WorkerBuckets { get; }

	public const string PanelType = "production-overview";
	public const string SourceKindSolePossessedRep = "solePossessedRep";
	public const string SourceKindExplicitEntity = "explicitEntity";
	public const string SourceKindEntityCollection = "entityCollection";
	public const string SourceKindControlPlaneView = "controlPlaneView";
	public const string QueueSourceCommandPanelSupplemental = "commandPanelSupplemental";
	public const string QueueSourceOrderBuffer = "orderBuffer";

	private void Validate()
	{
		switch (SourceKind)
		{
			case SourceKindSolePossessedRep:
			case SourceKindEntityCollection:
			case SourceKindControlPlaneView:
				if (string.IsNullOrWhiteSpace(SourceRef))
				{
					throw new InvalidOperationException(
						$"Production overview panel '{PanelId}' sourceKind '{SourceKind}' requires sourceRef.");
				}

				break;
			case SourceKindExplicitEntity:
				break;
			default:
				throw new InvalidOperationException(
					$"Production overview panel '{PanelId}' has unknown sourceKind '{SourceKind}'.");
		}

		switch (QueueSourceKind)
		{
			case QueueSourceCommandPanelSupplemental:
			case QueueSourceOrderBuffer:
				break;
			default:
				throw new InvalidOperationException(
					$"Production overview panel '{PanelId}' has unknown queueSourceKind '{QueueSourceKind}'.");
		}

		if (WorkerBuckets.Count > 0 && string.IsNullOrWhiteSpace(WorkerCollectionKey))
		{
			throw new InvalidOperationException(
				$"Production overview panel '{PanelId}' declares workerBuckets but missing workerCollectionKey.");
		}
	}

	private static string RequireId(string value, string paramName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException($"{paramName} is required.", paramName);
		}

		string trimmed = value.Trim();
		if (!string.Equals(value, trimmed, StringComparison.Ordinal))
		{
			throw new ArgumentException($"{paramName} must not contain leading or trailing whitespace.", paramName);
		}

		return trimmed;
	}
}

/// <summary>Stable worker-bucket reference for panel kit composition (ids only).</summary>
public sealed class ProductionOverviewWorkerBucketRef
{
	public ProductionOverviewWorkerBucketRef(string bucketId, string displayTokenId, string matchKind, string matchRef = "")
	{
		BucketId = RequireId(bucketId, nameof(bucketId));
		DisplayTokenId = RequireId(displayTokenId, nameof(displayTokenId));
		MatchKind = RequireId(matchKind, nameof(matchKind));
		MatchRef = matchRef?.Trim() ?? string.Empty;

		if (!string.Equals(MatchKind, "idle", StringComparison.Ordinal) &&
		    string.IsNullOrWhiteSpace(MatchRef))
		{
			throw new InvalidOperationException(
				$"Worker bucket '{BucketId}' matchKind '{MatchKind}' requires matchRef.");
		}
	}

	public string BucketId { get; }
	public string DisplayTokenId { get; }
	public string MatchKind { get; }
	public string MatchRef { get; }

	private static string RequireId(string value, string paramName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException($"{paramName} is required.", paramName);
		}

		string trimmed = value.Trim();
		if (!string.Equals(value, trimmed, StringComparison.Ordinal))
		{
			throw new ArgumentException($"{paramName} must not contain leading or trailing whitespace.", paramName);
		}

		return trimmed;
	}
}
