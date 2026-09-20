using System.Text.Json;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Registry;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.Core.UI.ProductionOverview;
using Ludots.WebUI.DataPlane;
using Ludots.WebUI.PanelKit;
using NUnit.Framework;

namespace Ludots.Tests.WebUiPanelKit;

/// <summary>
/// WPK-4 Production / Worker / Queue overview contract tests: project command/status/queue,
/// worker buckets from collection + tag/order/attribute, fail-fast missing sources/profile,
/// DataPlane payload shape, and no parallel production store.
/// </summary>
[TestFixture]
public sealed class ProductionOverviewPanelTests
{
	private const string ProfileId = "production.test.overview";
	private const string PanelSourceId = "test.production.source";
	private const string Topic = "panel-kit.test.production";
	private const string WorkerCollectionKey = "collection.workers";
	private const string GatherOrderKey = "order.sample.gather";
	private const string BuildingTag = "tag.sample.building";

	[SetUp]
	public void SetUp()
	{
		TagRegistry.Clear();
		AttributeRegistry.Clear();
	}

	[TearDown]
	public void TearDown()
	{
		TagRegistry.Clear();
		AttributeRegistry.Clear();
	}

	[Test]
	public void Project_ReadsCommandPanelStatusAndQueue_WithoutProductionStore()
	{
		using World world = World.Create();
		Entity owner = world.Create();
		Entity barracks = world.Create();
		var source = new StubProductionPanelSource();
		source.SetStatuses(
			new EntityCommandPanelStatusView(
				EntityCommandPanelStatusKind.ActiveAbility,
				420,
				"Train Infantry",
				"active",
				"#58B7FF"));
		source.SetQueue(
			new EntityCommandPanelQueueItemView(EntityCommandPanelQueueStage.Active, "Train Infantry", "active", "#58B7FF"),
			new EntityCommandPanelQueueItemView(EntityCommandPanelQueueStage.Queued, "Train Infantry", "queued", "#F2C36B"));

		Harness harness = Harness.Create(world, source);
		InstallCommandCollection(harness, owner, barracks);
		harness.InstallProfile(CreateBaseDefinition());

		var binding = new ProductionOverviewBindingContext(owner, Entity.Null, owner, EntityCollectionKeys.CommandSource);
		ProductionOverviewSnapshot snapshot = harness.Projector.Project(harness.Profiles.Require(ProfileId), in binding);

		Assert.That(snapshot.ProfileId, Is.EqualTo(ProfileId));
		Assert.That(snapshot.Revision, Is.GreaterThan(0u));
		Assert.That(snapshot.Rows, Has.Count.EqualTo(1));
		Assert.That(snapshot.Rows[0].Label, Is.EqualTo("Train Infantry"));
		Assert.That(snapshot.Rows[0].ProgressPermille, Is.EqualTo((short)420));
		Assert.That(snapshot.QueueItems, Has.Count.EqualTo(2));
		Assert.That(snapshot.QueueItems[0].Stage, Is.EqualTo(ProductionQueueStageIds.Active));
		Assert.That(snapshot.QueueItems[0].ProgressPermille, Is.EqualTo((short)420));
		Assert.That(snapshot.QueueItems[1].Stage, Is.EqualTo(ProductionQueueStageIds.Queued));
		Assert.That(source.StatusCopyCount, Is.GreaterThan(0));
		Assert.That(source.QueueCopyCount, Is.GreaterThan(0));
	}

	[Test]
	public void WorkerBuckets_ComeFromCollectionTagOrderAttributeProjection()
	{
		using World world = World.Create();
		Entity owner = world.Create();
		Entity idle = world.Create(new OrderBuffer { ActiveIndex = -1 });
		Entity gathering = world.Create(new OrderBuffer
		{
			ActiveIndex = 0,
			ActiveOrder = new QueuedOrder
			{
				Order = new Order { OrderTypeId = 7, OrderId = 1 }
			}
		});
		int buildingTagId = TagRegistry.Register(BuildingTag);
		Entity building = world.Create(new OrderBuffer { ActiveIndex = -1 }, new GameplayTagContainer());
		ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(building);
		tags.AddTag(buildingTagId);

		var source = new StubProductionPanelSource();
		Harness harness = Harness.Create(world, source, registerGatherOrder: true);
		InstallCommandCollection(harness, owner, idle);
		InstallWorkerCollection(harness, owner, idle, gathering, building);

		var definition = CreateBaseDefinition();
		definition.WorkerCollectionKey = WorkerCollectionKey;
		definition.WorkerBuckets =
		[
			new ProductionWorkerBucketDefinition
			{
				BucketId = "bucket.idle",
				DisplayTokenId = "token.worker.idle",
				MatchKind = ProductionWorkerMatchKindIds.Idle,
				SortOrder = 10
			},
			new ProductionWorkerBucketDefinition
			{
				BucketId = "bucket.gathering",
				DisplayTokenId = "token.worker.gathering",
				MatchKind = ProductionWorkerMatchKindIds.OrderType,
				MatchRef = GatherOrderKey,
				SortOrder = 20
			},
			new ProductionWorkerBucketDefinition
			{
				BucketId = "bucket.building",
				DisplayTokenId = "token.worker.building",
				MatchKind = ProductionWorkerMatchKindIds.Tag,
				MatchRef = BuildingTag,
				SortOrder = 30
			}
		];
		harness.InstallProfile(definition);

		var binding = new ProductionOverviewBindingContext(owner, Entity.Null, owner, EntityCollectionKeys.CommandSource);
		ProductionOverviewSnapshot snapshot = harness.Projector.Project(harness.Profiles.Require(ProfileId), in binding);

		Assert.That(snapshot.WorkerRows, Has.Count.EqualTo(3));
		Assert.That(FindWorker(snapshot, "bucket.idle").Count, Is.EqualTo(1));
		Assert.That(FindWorker(snapshot, "bucket.gathering").Count, Is.EqualTo(1));
		Assert.That(FindWorker(snapshot, "bucket.building").Count, Is.EqualTo(1));
	}

	[Test]
	public void QueueChange_UpdatesRevision()
	{
		using World world = World.Create();
		Entity owner = world.Create();
		Entity barracks = world.Create();
		var source = new StubProductionPanelSource();
		source.SetQueue(
			new EntityCommandPanelQueueItemView(EntityCommandPanelQueueStage.Active, "A", "", "#fff"));
		Harness harness = Harness.Create(world, source);
		InstallCommandCollection(harness, owner, barracks);
		harness.InstallProfile(CreateBaseDefinition());

		var binding = new ProductionOverviewBindingContext(owner, Entity.Null, owner, EntityCollectionKeys.CommandSource);
		ProductionOverviewSnapshot first = harness.Projector.Project(harness.Profiles.Require(ProfileId), in binding);

		source.SetQueue(
			new EntityCommandPanelQueueItemView(EntityCommandPanelQueueStage.Active, "A", "", "#fff"),
			new EntityCommandPanelQueueItemView(EntityCommandPanelQueueStage.Queued, "B", "", "#eee"));
		source.BumpRevision();
		ProductionOverviewSnapshot second = harness.Projector.Project(harness.Profiles.Require(ProfileId), in binding);

		Assert.That(second.Revision, Is.Not.EqualTo(first.Revision));
		Assert.That(second.QueueItems, Has.Count.EqualTo(2));
	}

	[Test]
	public void MissingCommandPanelSource_FailsFastWithConcreteId()
	{
		using World world = World.Create();
		var profileIds = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
		// Install without source registry (mods register sources after Core boot); project-time must fail.
		var profiles = new ProductionOverviewProfileRegistry(profileIds, commandPanelSources: null);
		profiles.Install(new ProductionOverviewProfilesConfig
		{
			Profiles = [CreateBaseDefinition()]
		});

		var emptySources = new SourceRegistry();
		var projector = new ProductionOverviewProjector(emptySources, world: world);
		Entity owner = world.Create();
		var binding = new ProductionOverviewBindingContext(owner, Entity.Null, owner, EntityCollectionKeys.CommandSource);

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			projector.Project(profiles.Require(ProfileId), in binding))!;

		Assert.That(ex.Message, Does.Contain(PanelSourceId));
		Assert.That(ex.Message, Does.Contain("unknown command panel source").IgnoreCase);
	}

	[Test]
	public void MissingProfile_FailsFastWithConcreteId()
	{
		var profileIds = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
		var profiles = new ProductionOverviewProfileRegistry(profileIds);

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			profiles.Require("production.missing.profile"))!;

		Assert.That(ex.Message, Does.Contain("production.missing.profile"));
	}

	[Test]
	public void MissingQueueSourceKind_FailsFastAtInstall()
	{
		var profileIds = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
		var profiles = new ProductionOverviewProfileRegistry(profileIds);
		var definition = CreateBaseDefinition();
		definition.QueueSourceKind = string.Empty;

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			profiles.Install(new ProductionOverviewProfilesConfig
			{
				Profiles = [definition]
			}))!;

		Assert.That(ex.Message, Does.Contain("queueSourceKind"));
	}

	[Test]
	public void EntityCollection_MissingSourceRefCollection_FailsFastWithoutOwnerFallback()
	{
		using World world = World.Create();
		Entity owner = world.Create();
		var source = new StubProductionPanelSource();
		source.SetStatuses(
			new EntityCommandPanelStatusView(
				EntityCommandPanelStatusKind.ActiveAbility,
				100,
				"ShouldNotAppear",
				"",
				"#fff"));

		Harness harness = Harness.Create(world, source);
		harness.InstallProfile(CreateBaseDefinition());

		var binding = new ProductionOverviewBindingContext(owner, Entity.Null, owner, EntityCollectionKeys.CommandSource);
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			harness.Projector.Project(harness.Profiles.Require(ProfileId), in binding))!;

		Assert.That(ex.Message, Does.Contain(ProfileId));
		Assert.That(ex.Message, Does.Contain(EntityCollectionKeys.CommandSource));
		Assert.That(ex.Message, Does.Contain("missing producer collection").IgnoreCase);
		Assert.That(source.StatusCopyCount, Is.EqualTo(0), "Must not fall back to owner and project owner statuses.");
	}

	[Test]
	public void EntityCollection_EmptySourceRefCollection_FailsFastWithoutOwnerFallback()
	{
		using World world = World.Create();
		Entity owner = world.Create();
		var source = new StubProductionPanelSource();
		source.SetStatuses(
			new EntityCommandPanelStatusView(
				EntityCommandPanelStatusKind.ActiveAbility,
				100,
				"ShouldNotAppear",
				"",
				"#fff"));

		Harness harness = Harness.Create(world, source);
		InstallCommandCollection(harness, owner);
		harness.InstallProfile(CreateBaseDefinition());

		var binding = new ProductionOverviewBindingContext(owner, Entity.Null, owner, EntityCollectionKeys.CommandSource);
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			harness.Projector.Project(harness.Profiles.Require(ProfileId), in binding))!;

		Assert.That(ex.Message, Does.Contain(ProfileId));
		Assert.That(ex.Message, Does.Contain(EntityCollectionKeys.CommandSource));
		Assert.That(ex.Message, Does.Contain("empty").IgnoreCase);
		Assert.That(source.StatusCopyCount, Is.EqualTo(0), "Must not fall back to owner and project owner statuses.");
	}

	[Test]
	public void ExplicitEntity_UsesOwnerAsSoleProducer()
	{
		using World world = World.Create();
		Entity focused = world.Create();
		var source = new StubProductionPanelSource();
		source.SetStatuses(
			new EntityCommandPanelStatusView(
				EntityCommandPanelStatusKind.ActiveAbility,
				250,
				"Focused Train",
				"active",
				"#58B7FF"));

		Harness harness = Harness.Create(world, source);
		var definition = CreateBaseDefinition();
		definition.SourceKind = ProductionOverviewSourceKindIds.ExplicitEntity;
		definition.SourceRef = string.Empty;
		harness.InstallProfile(definition);

		var binding = new ProductionOverviewBindingContext(Entity.Null, focused, Entity.Null, string.Empty);
		ProductionOverviewSnapshot snapshot = harness.Projector.Project(harness.Profiles.Require(ProfileId), in binding);

		Assert.That(snapshot.Rows, Has.Count.EqualTo(1));
		Assert.That(snapshot.Rows[0].Label, Is.EqualTo("Focused Train"));
		Assert.That(snapshot.Rows[0].OwnerEntityId, Is.EqualTo(focused.Id));
	}

	[Test]
	public void LocalPlayerRep_UsesOwnerAsSoleProducer()
	{
		using World world = World.Create();
		Entity player = world.Create();
		var source = new StubProductionPanelSource();
		source.SetStatuses(
			new EntityCommandPanelStatusView(
				EntityCommandPanelStatusKind.ActiveAbility,
				300,
				"Player Train",
				"active",
				"#58B7FF"));

		Harness harness = Harness.Create(world, source);
		var definition = CreateBaseDefinition();
		definition.SourceKind = ProductionOverviewSourceKindIds.SolePossessedRep;
		definition.SourceRef = EntityCollectionKeys.CommandSource;
		harness.InstallProfile(definition);

		var binding = new ProductionOverviewBindingContext(player, Entity.Null, Entity.Null, string.Empty);
		ProductionOverviewSnapshot snapshot = harness.Projector.Project(harness.Profiles.Require(ProfileId), in binding);

		Assert.That(snapshot.Rows, Has.Count.EqualTo(1));
		Assert.That(snapshot.Rows[0].Label, Is.EqualTo("Player Train"));
		Assert.That(snapshot.Rows[0].OwnerEntityId, Is.EqualTo(player.Id));
	}

	[Test]
	public void TopicProducer_Payload_ContainsOwnerProfileRevisionRowsQueueAndWorkers()
	{
		using World world = World.Create();
		Entity owner = world.Create();
		Entity barracks = world.Create();
		var source = new StubProductionPanelSource();
		source.SetStatuses(
			new EntityCommandPanelStatusView(EntityCommandPanelStatusKind.ActiveAbility, 100, "Train", "", "#fff"));
		source.SetQueue(
			new EntityCommandPanelQueueItemView(EntityCommandPanelQueueStage.Active, "Train", "", "#fff"));

		Harness harness = Harness.Create(world, source);
		InstallCommandCollection(harness, owner, barracks);
		var definition = CreateBaseDefinition();
		definition.Topic = Topic;
		harness.InstallProfile(definition);

		var producer = new ProductionOverviewWebUiTopicProducer(
			Topic,
			harness.Projector,
			harness.Profiles.Require(ProfileId),
			() => new ProductionOverviewBindingContext(owner, Entity.Null, owner, EntityCollectionKeys.CommandSource));

		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(producer);
		Assert.That(runtime.IsTopicRegistered(Topic), Is.True);

		var context = new WebUiTopicContext("session-a", Topic, 3, JsonSerializer.SerializeToElement(new { }));
		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		Assert.That(packet.ContentType, Is.EqualTo(ProductionOverviewWebUiTopicProducer.JsonContentType));

		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		JsonElement root = document.RootElement;
		Assert.That(root.GetProperty("profileId").GetString(), Is.EqualTo(ProfileId));
		Assert.That(root.GetProperty("ownerEntityId").GetInt32(), Is.EqualTo(owner.Id));
		Assert.That(root.GetProperty("ownerVersion").GetInt32(), Is.EqualTo(owner.Version));
		Assert.That(root.GetProperty("revision").GetUInt32(), Is.GreaterThan(0u));
		Assert.That(root.GetProperty("rows").GetArrayLength(), Is.EqualTo(1));
		Assert.That(root.GetProperty("queueItems").GetArrayLength(), Is.EqualTo(1));
		Assert.That(root.GetProperty("workerRows").GetArrayLength(), Is.EqualTo(0));
		Assert.That(root.TryGetProperty("blockedReasons", out _), Is.True);
	}

	[Test]
	public void PanelDescriptor_RequiresCommandAndQueueSource()
	{
		Assert.Throws<ArgumentException>(() => new ProductionOverviewPanelDescriptor(
			"hud.production",
			ProductionOverviewPanelDescriptor.SourceKindEntityCollection,
			EntityCollectionKeys.CommandSource,
			commandPanelSourceId: "",
			ProductionOverviewPanelDescriptor.QueueSourceCommandPanelSupplemental,
			Topic,
			ProfileId,
			"layout.overview.split"));

		Assert.Throws<InvalidOperationException>(() => new ProductionOverviewPanelDescriptor(
			"hud.production",
			ProductionOverviewPanelDescriptor.SourceKindEntityCollection,
			EntityCollectionKeys.CommandSource,
			PanelSourceId,
			queueSourceKind: "inventedQueue",
			Topic,
			ProfileId,
			"layout.overview.split"));
	}

	private static ProductionOverviewProfileDefinition CreateBaseDefinition()
	{
		return new ProductionOverviewProfileDefinition
		{
			Id = ProfileId,
			SourceKind = ProductionOverviewSourceKindIds.EntityCollection,
			SourceRef = EntityCollectionKeys.CommandSource,
			CommandPanelSourceId = PanelSourceId,
			QueueSourceKind = ProductionQueueSourceKindIds.CommandPanelSupplemental,
			WorkerBuckets = [],
			Topic = Topic
		};
	}

	private static void InstallCommandCollection(Harness harness, Entity owner, params Entity[] members)
	{
		var descriptor = EntityCollectionDescriptor.Create(
			EntityCollectionKeys.CommandSource,
			EntityCollectionSourceKind.Explicit,
			EntityCollectionRoleKind.CommandSource,
			owner,
			members.Length > 0 ? members[0] : Entity.Null);
		harness.Collections.Replace(owner, descriptor, members);
	}

	private static void InstallWorkerCollection(Harness harness, Entity owner, params Entity[] members)
	{
		harness.CollectionKeys.Register(WorkerCollectionKey);
		var descriptor = EntityCollectionDescriptor.Create(
			WorkerCollectionKey,
			EntityCollectionSourceKind.Explicit,
			EntityCollectionRoleKind.Display,
			owner,
			members.Length > 0 ? members[0] : Entity.Null);
		harness.Collections.Replace(owner, descriptor, members);
	}

	private static ProductionOverviewWorkerRow FindWorker(ProductionOverviewSnapshot snapshot, string bucketId)
	{
		foreach (ProductionOverviewWorkerRow row in snapshot.WorkerRows)
		{
			if (string.Equals(row.BucketId, bucketId, StringComparison.Ordinal))
			{
				return row;
			}
		}

		throw new AssertionException($"Missing worker bucket '{bucketId}'.");
	}

	private sealed class Harness
	{
		public required ProductionOverviewProfileRegistry Profiles { get; init; }
		public required ProductionOverviewProjector Projector { get; init; }
		public required EntityCollectionStore Collections { get; init; }
		public required StringIntRegistry CollectionKeys { get; init; }
		public required StubProductionPanelSource Source { get; init; }

		public static Harness Create(World world, StubProductionPanelSource source, bool registerGatherOrder = false)
		{
			var sources = new SourceRegistry();
			sources.Register(PanelSourceId, source);

			var collectionKeys = new StringIntRegistry(32, 1, 0, StringComparer.Ordinal);
			collectionKeys.Register(EntityCollectionKeys.CommandSource);
			var collections = new EntityCollectionStore(collectionKeys, 32, 64);

			OrderTypeRegistry? orderTypes = null;
			if (registerGatherOrder)
			{
				orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
				orderTypes.Register(new OrderTypeConfig
				{
					Key = GatherOrderKey,
					OrderTypeId = 7,
					Label = "Gather"
				});
			}

			var profileIds = new StringIntRegistry(16, 1, 0, StringComparer.Ordinal);
			var profiles = new ProductionOverviewProfileRegistry(
				profileIds,
				sources,
				orderTypes,
				displayToken => displayToken.StartsWith("token.", StringComparison.Ordinal));

			var projector = new ProductionOverviewProjector(
				sources,
				collections,
				controlPlane: null,
				collectionKeys,
				orderTypes,
				world);

			return new Harness
			{
				Profiles = profiles,
				Projector = projector,
				Collections = collections,
				CollectionKeys = collectionKeys,
				Source = source
			};
		}

		public void InstallProfile(ProductionOverviewProfileDefinition definition)
		{
			Profiles.Install(new ProductionOverviewProfilesConfig
			{
				Profiles = [definition]
			});
		}
	}

	private sealed class SourceRegistry : IEntityCommandPanelSourceRegistry
	{
		private readonly Dictionary<string, IEntityCommandPanelSource> _map = new(StringComparer.Ordinal);

		public void Register(string sourceId, IEntityCommandPanelSource source)
		{
			_map[sourceId] = source;
		}

		public bool TryGet(string sourceId, out IEntityCommandPanelSource source)
		{
			return _map.TryGetValue(sourceId, out source!);
		}
	}

	private sealed class StubProductionPanelSource :
		IEntityCommandPanelContextSource,
		IEntityCommandPanelContextSupplementalSource
	{
		private EntityCommandPanelStatusView[] _statuses = [];
		private EntityCommandPanelQueueItemView[] _queue = [];
		private uint _revision = 1;

		public int StatusCopyCount { get; private set; }
		public int QueueCopyCount { get; private set; }

		public void SetStatuses(params EntityCommandPanelStatusView[] statuses)
		{
			_statuses = statuses;
		}

		public void SetQueue(params EntityCommandPanelQueueItemView[] queue)
		{
			_queue = queue;
		}

		public void BumpRevision()
		{
			_revision++;
		}

		public bool TryGetRevision(Entity target, out uint revision) => TryGetRevision(default, out revision);

		public bool TryGetRevision(in EntityCommandPanelSourceContext context, out uint revision)
		{
			revision = _revision;
			return true;
		}

		public int GetGroupCount(Entity target) => 1;
		public int GetGroupCount(in EntityCommandPanelSourceContext context) => 1;

		public bool TryGetGroup(Entity target, int groupIndex, out EntityCommandPanelGroupView group) =>
			TryGetGroup(default, groupIndex, out group);

		public bool TryGetGroup(in EntityCommandPanelSourceContext context, int groupIndex, out EntityCommandPanelGroupView group)
		{
			group = new EntityCommandPanelGroupView(0, "default", 0);
			return groupIndex == 0;
		}

		public int CopySlots(Entity target, int groupIndex, Span<EntityCommandPanelSlotView> destination) => 0;

		public int CopySlots(in EntityCommandPanelSourceContext context, int groupIndex, Span<EntityCommandPanelSlotView> destination) => 0;

		public int CopyStatuses(Entity target, Span<EntityCommandPanelStatusView> destination) =>
			CopyStatuses(default, destination);

		public int CopyStatuses(in EntityCommandPanelSourceContext context, Span<EntityCommandPanelStatusView> destination)
		{
			StatusCopyCount++;
			int count = Math.Min(_statuses.Length, destination.Length);
			for (int i = 0; i < count; i++)
			{
				destination[i] = _statuses[i];
			}

			return count;
		}

		public int CopyQueueItems(Entity target, Span<EntityCommandPanelQueueItemView> destination) =>
			CopyQueueItems(default, destination);

		public int CopyQueueItems(in EntityCommandPanelSourceContext context, Span<EntityCommandPanelQueueItemView> destination)
		{
			QueueCopyCount++;
			int count = Math.Min(_queue.Length, destination.Length);
			for (int i = 0; i < count; i++)
			{
				destination[i] = _queue[i];
			}

			return count;
		}
	}
}
