using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using Ludots.Core.UI.CommandDeck;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.WebUI.DataPlane;
using Ludots.WebUI.PanelKit;
using NUnit.Framework;

namespace Ludots.Tests.WebUiPanelKit;

/// <summary>
/// WPK-3 CommandDeck multi-display-mode contract tests: four modes, route profile selection,
/// fail-fast missing refs, DataPlane payload shape, and no SelectionRuntime authority.
/// </summary>
[TestFixture]
public sealed class CommandDeckDisplayModeTests
{
	private const string GlobalProfileId = "command-deck.test.global";
	private const string EntityProfileId = "command-deck.test.entity";
	private const string AggregateProfileId = "command-deck.test.aggregate";
	private const string PinnedProfileId = "command-deck.test.pinned";
	private const string RouteNearestId = "dispatch.nearest_top_n";
	private const string AggregationByAbilityId = "aggregation.by_ability_id";
	private const string PanelSourceId = "test.command-deck.source";
	private const string Topic = "panel-kit.test.command-deck";

	[Test]
	public void GlobalMode_WithoutFocusedEntity_ProjectsPlayerSourceCommands()
	{
		using var world = World.Create();
		Entity player = world.Create();
		var source = new StubCommandPanelSource();
		source.SetSlots(
			new EntityCommandPanelSlotView(0, 11, 0, EntityCommandSlotStateFlags.Base, 0, 0, 0, "Build Barracks", "ready", "category.build"));
		Harness harness = Harness.Create(source, installRoute: false);

		harness.InstallProfile(new CommandDeckProfileDefinition
		{
			Id = GlobalProfileId,
			DisplayMode = CommandDeckDisplayModeIds.Global,
			SourceKind = CommandDeckSourceKindIds.LocalPlayerRep,
			SourceRef = "collection.command.source",
			CommandPanelSourceId = PanelSourceId,
			Topic = Topic
		});

		var binding = new CommandDeckBindingContext(
			localPlayerRep: player,
			focusedEntity: Entity.Null,
			collectionOwner: player,
			instanceKey: "collection.command.source",
			visibilityConditionSatisfied: false);

		CommandDeckSnapshot snapshot = harness.Projector.Project(harness.Profiles.Require(GlobalProfileId), in binding);

		Assert.That(snapshot.DisplayMode, Is.EqualTo(CommandDeckDisplayMode.Global));
		Assert.That(snapshot.Visible, Is.True);
		Assert.That(snapshot.Entries, Has.Count.EqualTo(1));
		Assert.That(snapshot.Entries[0].DisplayLabel, Is.EqualTo("Build Barracks"));
		Assert.That(snapshot.Entries[0].CategoryId, Is.EqualTo("category.build"));
		Assert.That(snapshot.Revision, Is.GreaterThan(0u));
	}

	[Test]
	public void EntityMode_UsesExplicitFocusedEntity_NotSelectionRuntime()
	{
		using var world = World.Create();
		Entity focused = world.Create();
		var source = new StubCommandPanelSource();
		source.SetSlots(
			new EntityCommandPanelSlotView(0, 21, 0, EntityCommandSlotStateFlags.Base, 0, 0, 0, "Repair", "", "category.build"));
		Harness harness = Harness.Create(source, installRoute: false);

		harness.InstallProfile(new CommandDeckProfileDefinition
		{
			Id = EntityProfileId,
			DisplayMode = CommandDeckDisplayModeIds.Entity,
			SourceKind = CommandDeckSourceKindIds.ExplicitEntity,
			CommandPanelSourceId = PanelSourceId,
			Topic = Topic
		});

		var binding = new CommandDeckBindingContext(
			localPlayerRep: Entity.Null,
			focusedEntity: focused,
			collectionOwner: Entity.Null,
			instanceKey: string.Empty,
			visibilityConditionSatisfied: false);

		CommandDeckSnapshot snapshot = harness.Projector.Project(harness.Profiles.Require(EntityProfileId), in binding);

		Assert.That(snapshot.DisplayMode, Is.EqualTo(CommandDeckDisplayMode.Entity));
		Assert.That(snapshot.Entries, Has.Count.EqualTo(1));
		Assert.That(snapshot.Entries[0].DisplayLabel, Is.EqualTo("Repair"));
		Assert.That(source.LastTarget, Is.EqualTo(focused));
	}

	[Test]
	public void AggregateFiltered_ExposesOwnerCountStatusAndBlockedReason()
	{
		using var world = World.Create();
		Entity owner = world.Create();
		Entity memberA = world.Create(Ludots.Core.Components.WorldPositionCm.FromCm(10, 0));
		Entity memberB = world.Create(Ludots.Core.Components.WorldPositionCm.FromCm(20, 0));
		Entity memberC = world.Create(Ludots.Core.Components.WorldPositionCm.FromCm(30, 0));
		var source = new StubCommandPanelSource();
		source.SetSlots(
			new EntityCommandPanelSlotView(
				0,
				31,
				0,
				EntityCommandSlotStateFlags.Base | EntityCommandSlotStateFlags.Blocked,
				500,
				0,
				0,
				"Train Infantry",
				"3 owners | missing minerals",
				"category.train"));
		source.SetAggregationMembers(0,
			new EntityCommandPanelAggregationMember(memberA, 0),
			new EntityCommandPanelAggregationMember(memberB, 0),
			new EntityCommandPanelAggregationMember(memberC, 0));
		Harness harness = Harness.Create(source, installRoute: true);

		harness.InstallProfile(new CommandDeckProfileDefinition
		{
			Id = AggregateProfileId,
			DisplayMode = CommandDeckDisplayModeIds.AggregateFiltered,
			SourceKind = CommandDeckSourceKindIds.EntityCollection,
			SourceRef = "collection.command.source",
			CommandPanelSourceId = PanelSourceId,
			AggregationProfileId = AggregationByAbilityId,
			RouteProfileId = RouteNearestId,
			CategoryTagPrefix = "category.",
			Topic = Topic
		});

		var binding = new CommandDeckBindingContext(owner, Entity.Null, owner, "collection.command.source", false);
		CommandDeckSnapshot snapshot = harness.Projector.Project(
			harness.Profiles.Require(AggregateProfileId),
			in binding,
			world,
			routeTargetWorldCm: new Vector3(0f, 0f, 0f));

		Assert.That(snapshot.DisplayMode, Is.EqualTo(CommandDeckDisplayMode.AggregateFiltered));
		Assert.That(snapshot.Entries, Has.Count.EqualTo(1));
		Assert.That(snapshot.Entries[0].OwnerCount, Is.EqualTo(3));
		Assert.That(snapshot.Entries[0].Status, Is.EqualTo("blocked"));
		Assert.That(snapshot.Entries[0].BlockedReason, Is.EqualTo("3 owners | missing minerals"));
		Assert.That(snapshot.Entries[0].CategoryId, Is.EqualTo("category.train"));
		Assert.That(snapshot.Entries[0].RouteProfileId, Is.EqualTo(RouteNearestId));
		Assert.That(snapshot.Entries[0].RoutedOwnerEntityId, Is.EqualTo(memberA.Id));
	}

	[Test]
	public void ConditionalPinned_AppearsWhenConditionHolds_AndRevisionChangesWhenCleared()
	{
		using var world = World.Create();
		Entity player = world.Create();
		var source = new StubCommandPanelSource();
		source.SetSlots(
			new EntityCommandPanelSlotView(0, 41, 0, EntityCommandSlotStateFlags.Base, 0, 1, 1, "Superweapon", "", "category.superweapon"));
		Harness harness = Harness.Create(source, installRoute: false);

		harness.InstallProfile(new CommandDeckProfileDefinition
		{
			Id = PinnedProfileId,
			DisplayMode = CommandDeckDisplayModeIds.ConditionalPinned,
			SourceKind = CommandDeckSourceKindIds.LocalPlayerRep,
			SourceRef = "collection.command.source",
			CommandPanelSourceId = PanelSourceId,
			VisibilityConditionId = CommandDeckVisibilityConditionIds.BindingFlag,
			Topic = Topic
		});

		CommandDeckProfile profile = harness.Profiles.Require(PinnedProfileId);
		var visibleBinding = new CommandDeckBindingContext(player, Entity.Null, player, "collection.command.source", true);
		CommandDeckSnapshot visible = harness.Projector.Project(profile, in visibleBinding);
		Assert.That(visible.Visible, Is.True);
		Assert.That(visible.Entries, Has.Count.EqualTo(1));

		var hiddenBinding = new CommandDeckBindingContext(player, Entity.Null, player, "collection.command.source", false);
		CommandDeckSnapshot hidden = harness.Projector.Project(profile, in hiddenBinding);
		Assert.That(hidden.Visible, Is.False);
		Assert.That(hidden.Entries, Is.Empty);
		Assert.That(hidden.Revision, Is.Not.EqualTo(visible.Revision));
	}

	[Test]
	public void RouteResolver_NearestTopN_DoesNotSilentlyPickFirstMember()
	{
		using var world = World.Create();
		Entity far = world.Create(Ludots.Core.Components.WorldPositionCm.FromCm(10000, 0));
		Entity near = world.Create(Ludots.Core.Components.WorldPositionCm.FromCm(100, 0));
		var source = new StubCommandPanelSource();
		Harness harness = Harness.Create(source, installRoute: true);

		harness.InstallProfile(new CommandDeckProfileDefinition
		{
			Id = AggregateProfileId,
			DisplayMode = CommandDeckDisplayModeIds.AggregateFiltered,
			SourceKind = CommandDeckSourceKindIds.EntityCollection,
			SourceRef = "collection.command.source",
			CommandPanelSourceId = PanelSourceId,
			AggregationProfileId = AggregationByAbilityId,
			RouteProfileId = RouteNearestId,
			Topic = Topic
		});

		Span<CommandDeckRouteMember> members = stackalloc CommandDeckRouteMember[2];
		members[0] = new CommandDeckRouteMember(far, 0);
		members[1] = new CommandDeckRouteMember(near, 1);

		CommandDeckRouteTarget target = harness.Projector.ResolveActivationRoute(
			harness.Profiles.Require(AggregateProfileId),
			members,
			world,
			new Vector3(0f, 0f, 0f),
			groupKey: 99L);

		Assert.That(target.Owner, Is.EqualTo(near), "Route profile must select nearest member, not the first.");
		Assert.That(target.SlotIndex, Is.EqualTo(1));
	}

	[Test]
	public void Project_AggregateFiltered_RoutesFromFullMemberSet_NotFirstOwner()
	{
		using var world = World.Create();
		Entity owner = world.Create();
		Entity far = world.Create(Ludots.Core.Components.WorldPositionCm.FromCm(10000, 0));
		Entity near = world.Create(Ludots.Core.Components.WorldPositionCm.FromCm(100, 0));
		var source = new StubCommandPanelSource();
		source.SetSlots(
			new EntityCommandPanelSlotView(
				0,
				31,
				0,
				EntityCommandSlotStateFlags.Base,
				0,
				0,
				0,
				"Train Infantry",
				"2 owners | ready",
				"category.train"));
		// First member is farther; route profile must pick the nearer second member in Project payload.
		source.SetAggregationMembers(0,
			new EntityCommandPanelAggregationMember(far, 0),
			new EntityCommandPanelAggregationMember(near, 1));
		Harness harness = Harness.Create(source, installRoute: true);

		harness.InstallProfile(new CommandDeckProfileDefinition
		{
			Id = AggregateProfileId,
			DisplayMode = CommandDeckDisplayModeIds.AggregateFiltered,
			SourceKind = CommandDeckSourceKindIds.EntityCollection,
			SourceRef = "collection.command.source",
			CommandPanelSourceId = PanelSourceId,
			AggregationProfileId = AggregationByAbilityId,
			RouteProfileId = RouteNearestId,
			CategoryTagPrefix = "category.",
			Topic = Topic
		});

		var binding = new CommandDeckBindingContext(owner, Entity.Null, owner, "collection.command.source", false);
		CommandDeckSnapshot snapshot = harness.Projector.Project(
			harness.Profiles.Require(AggregateProfileId),
			in binding,
			world,
			routeTargetWorldCm: new Vector3(0f, 0f, 0f));

		Assert.That(snapshot.Entries, Has.Count.EqualTo(1));
		Assert.That(snapshot.Entries[0].RoutedOwnerEntityId, Is.EqualTo(near.Id),
			"Project payload must route via CastDispatch over the full member set, not the first/far member.");
		Assert.That(snapshot.Entries[0].RoutedSlotIndex, Is.EqualTo(1));
		Assert.That(snapshot.Entries[0].OwnerCount, Is.EqualTo(2));
	}

	[Test]
	public void Project_FilterProfile_HidesNonMatchingCategoryCommands()
	{
		using var world = World.Create();
		TagRegistry.Clear();
		try
		{
			int trainTag = TagRegistry.Register("category.train");
			int researchTag = TagRegistry.Register("category.research");
			Entity player = world.Create();
			Entity barracks = world.Create(new GameplayTagContainer());
			Entity lab = world.Create(new GameplayTagContainer());
			world.Get<GameplayTagContainer>(barracks).AddTag(trainTag);
			world.Get<GameplayTagContainer>(lab).AddTag(researchTag);

			var keyRegistry = new StringIntRegistry(16, 1, 0, StringComparer.Ordinal);
			keyRegistry.Register("collection.command.source");
			keyRegistry.Register(EntityViewKeys.CommandDeckFiltered);
			var collections = new EntityCollectionStore(keyRegistry, 8, 32);
			var descriptor = EntityCollectionDescriptor.Create(
				"collection.command.source",
				EntityCollectionSourceKind.Explicit,
				EntityCollectionRoleKind.CommandSource,
				contextEntity: player,
				primaryEntity: barracks,
				title: "commands");
			collections.Replace(player, descriptor, new[] { barracks, lab });

			var source = new StubCommandPanelSource();
			source.SetSlots(
				new EntityCommandPanelSlotView(0, 1, 0, EntityCommandSlotStateFlags.Base, 0, 0, 0, "Train", "", "category.train"),
				new EntityCommandPanelSlotView(1, 2, 0, EntityCommandSlotStateFlags.Base, 0, 0, 0, "Research", "", "category.research"));
			// After filter, projector materializes survivors into EntityViewKeys.CommandDeckFiltered.
			source.SlotProvider = ctx =>
			{
				Assert.That(ctx.InstanceKey, Is.EqualTo(EntityViewKeys.CommandDeckFiltered),
					"FilterProfile must redirect panel source to CommandDeck-owned filtered view key.");
				if (!collections.TryGet(ctx.TargetEntity, EntityViewKeys.CommandDeckFiltered, out var handle))
				{
					return Array.Empty<EntityCommandPanelSlotView>();
				}

				var members = new Entity[8];
				int count = collections.CopyEntities(handle, 0, members);
				var slots = new List<EntityCommandPanelSlotView>();
				for (int i = 0; i < count; i++)
				{
					if (members[i] == barracks)
					{
						slots.Add(new EntityCommandPanelSlotView(0, 1, 0, EntityCommandSlotStateFlags.Base, 0, 0, 0, "Train", "", "category.train"));
					}
					else if (members[i] == lab)
					{
						slots.Add(new EntityCommandPanelSlotView(0, 2, 0, EntityCommandSlotStateFlags.Base, 0, 0, 0, "Research", "", "category.research"));
					}
				}

				return slots.ToArray();
			};

			const string filterId = "filter.command-deck.train-only";
			var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
			var filterIds = new StringIntRegistry(16, 1, 0, StringComparer.Ordinal);
			var filters = new FilterProfileRegistry(filterIds, world, tagOps);
			filters.Install(new FilterProfilesConfig
			{
				Profiles = new List<FilterProfileDefinition>
				{
					new()
					{
						Id = filterId,
						AssociationQuery = new FilterProfileAssociationQuery
						{
							Anchor = FilterAnchorKinds.LocalPlayerRep,
							Expand = FilterAssociationExpandKinds.None
						},
						Include = new FilterProfileTagRule { AnyTags = new List<string> { "category.train" } }
					}
				}
			});

			Harness harness = Harness.Create(source, installRoute: false, collections: collections, collectionKeys: keyRegistry, filterProfiles: filters);
			harness.InstallProfile(new CommandDeckProfileDefinition
			{
				Id = AggregateProfileId,
				DisplayMode = CommandDeckDisplayModeIds.Global,
				SourceKind = CommandDeckSourceKindIds.EntityCollection,
				SourceRef = "collection.command.source",
				CommandPanelSourceId = PanelSourceId,
				FilterProfileId = filterId,
				CategoryTagPrefix = "category.",
				Topic = Topic
			});

			var binding = new CommandDeckBindingContext(player, Entity.Null, player, "collection.command.source", false);
			CommandDeckSnapshot snapshot = harness.Projector.Project(harness.Profiles.Require(AggregateProfileId), in binding);

			Assert.That(snapshot.Entries, Has.Count.EqualTo(1));
			Assert.That(snapshot.Entries[0].CategoryId, Is.EqualTo("category.train"));
			Assert.That(snapshot.Entries[0].DisplayLabel, Is.EqualTo("Train"));

			Assert.That(collections.TryGet(player, "collection.command.source", out EntityCollectionHandle originalHandle), Is.True);
			var originalMembers = new Entity[8];
			int originalCount = collections.CopyEntities(originalHandle, 0, originalMembers);
			Assert.That(originalCount, Is.EqualTo(2), "Filter must not overwrite the original sourceRef collection.");
			Assert.That(originalMembers.AsSpan(0, originalCount).ToArray(), Is.EquivalentTo(new[] { barracks, lab }));
		}
		finally
		{
			TagRegistry.Clear();
		}
	}

	[Test]
	public void Project_FilterProfile_DoesNotMutateOriginalSourceCollection()
	{
		using var world = World.Create();
		TagRegistry.Clear();
		try
		{
			int trainTag = TagRegistry.Register("category.train");
			int researchTag = TagRegistry.Register("category.research");
			Entity player = world.Create();
			Entity barracks = world.Create(new GameplayTagContainer());
			Entity lab = world.Create(new GameplayTagContainer());
			world.Get<GameplayTagContainer>(barracks).AddTag(trainTag);
			world.Get<GameplayTagContainer>(lab).AddTag(researchTag);

			const string sourceKey = "collection.command.source";
			var keyRegistry = new StringIntRegistry(16, 1, 0, StringComparer.Ordinal);
			keyRegistry.Register(sourceKey);
			keyRegistry.Register(EntityViewKeys.CommandDeckFiltered);
			var collections = new EntityCollectionStore(keyRegistry, 8, 32);
			collections.Replace(
				player,
				EntityCollectionDescriptor.Create(
					sourceKey,
					EntityCollectionSourceKind.Explicit,
					EntityCollectionRoleKind.CommandSource,
					contextEntity: player,
					primaryEntity: barracks,
					title: "commands"),
				new[] { barracks, lab });

			var source = new StubCommandPanelSource();
			source.SlotProvider = ctx =>
			{
				if (!collections.TryGet(ctx.TargetEntity, ctx.InstanceKey, out EntityCollectionHandle handle))
				{
					return Array.Empty<EntityCommandPanelSlotView>();
				}

				var members = new Entity[8];
				int count = collections.CopyEntities(handle, 0, members);
				var slots = new List<EntityCommandPanelSlotView>();
				for (int i = 0; i < count; i++)
				{
					if (members[i] == barracks)
					{
						slots.Add(new EntityCommandPanelSlotView(0, 1, 0, EntityCommandSlotStateFlags.Base, 0, 0, 0, "Train", "", "category.train"));
					}
					else if (members[i] == lab)
					{
						slots.Add(new EntityCommandPanelSlotView(0, 2, 0, EntityCommandSlotStateFlags.Base, 0, 0, 0, "Research", "", "category.research"));
					}
				}

				return slots.ToArray();
			};

			const string filterId = "filter.command-deck.train-only.preserve-source";
			var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
			var filterIds = new StringIntRegistry(16, 1, 0, StringComparer.Ordinal);
			var filters = new FilterProfileRegistry(filterIds, world, tagOps);
			filters.Install(new FilterProfilesConfig
			{
				Profiles = new List<FilterProfileDefinition>
				{
					new()
					{
						Id = filterId,
						AssociationQuery = new FilterProfileAssociationQuery
						{
							Anchor = FilterAnchorKinds.LocalPlayerRep,
							Expand = FilterAssociationExpandKinds.None
						},
						Include = new FilterProfileTagRule { AnyTags = new List<string> { "category.train" } }
					}
				}
			});

			Harness harness = Harness.Create(source, installRoute: false, collections: collections, collectionKeys: keyRegistry, filterProfiles: filters);
			harness.InstallProfile(new CommandDeckProfileDefinition
			{
				Id = AggregateProfileId,
				DisplayMode = CommandDeckDisplayModeIds.Global,
				SourceKind = CommandDeckSourceKindIds.EntityCollection,
				SourceRef = sourceKey,
				CommandPanelSourceId = PanelSourceId,
				FilterProfileId = filterId,
				CategoryTagPrefix = "category.",
				Topic = Topic
			});

			var binding = new CommandDeckBindingContext(player, Entity.Null, player, sourceKey, false);
			CommandDeckSnapshot snapshot = harness.Projector.Project(harness.Profiles.Require(AggregateProfileId), in binding);

			Assert.That(snapshot.Entries, Has.Count.EqualTo(1));
			Assert.That(snapshot.Entries[0].DisplayLabel, Is.EqualTo("Train"));
			Assert.That(source.LastTarget, Is.EqualTo(player));

			Assert.That(collections.TryGet(player, sourceKey, out EntityCollectionHandle originalHandle), Is.True);
			var originalMembers = new Entity[8];
			int originalCount = collections.CopyEntities(originalHandle, 0, originalMembers);
			Assert.That(originalCount, Is.EqualTo(2));
			Assert.That(originalMembers.AsSpan(0, originalCount).ToArray(), Is.EquivalentTo(new[] { barracks, lab }));

			Assert.That(collections.TryGet(player, EntityViewKeys.CommandDeckFiltered, out EntityCollectionHandle filteredHandle), Is.True);
			var filteredMembers = new Entity[8];
			int filteredCount = collections.CopyEntities(filteredHandle, 0, filteredMembers);
			Assert.That(filteredCount, Is.EqualTo(1));
			Assert.That(filteredMembers[0], Is.EqualTo(barracks));
		}
		finally
		{
			TagRegistry.Clear();
		}
	}

	[Test]
	public void ControlPlaneView_ExpandShrink_ChangesOwnerCountAndRevision()
	{
		using var world = World.Create();
		var types = new RelationshipTypeRegistry();
		var relationships = new RelationshipRuntime(
			world,
			types,
			new RelationshipMetricRegistry(),
			new RelationshipFlagRegistry(),
			new RelationshipBandRegistry(),
			new RelationshipChangeBuffer(capacity: 4),
			new RelationshipReverseIndex(world));
		int ownsTypeId = types.Register("Owns");
		int controlsTypeId = types.Register("Controls");
		var ownership = new OwnershipResolver(relationships, ownsTypeId);
		var query = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId);
		var keyRegistry = new StringIntRegistry(16, 1, 0, StringComparer.Ordinal);
		int keyId = keyRegistry.Register("collection.command.source");
		keyRegistry.Register(EntityViewKeys.ControlPlaneCommand);
		var collections = new EntityCollectionStore(keyRegistry, 8, 32);
		var writer = new DomainRoutedCollectionWriter(collections, query);
		var controlPlane = new ControlPlaneView(collections, query);

		Entity p1 = world.Create(new PlayerIdentity { PlayerId = 1 });
		Entity p2 = world.Create(new PlayerIdentity { PlayerId = 2 });
		Entity m01 = world.Create();
		Entity m99 = world.Create();
		ownership.EnsureOwnership(p1, m01);
		ownership.EnsureOwnership(p2, m99);

		writer.ReplaceRouted(
			p1,
			keyId,
			stackalloc Entity[] { m01 },
			EntityCollectionSourceKind.UiAcquisition,
			DomainRoutingUnresolvedPolicy.Reject);

		var source = new StubCommandPanelSource();
		source.RevisionProvider = ctx =>
		{
			uint rev = controlPlane.ComputeRevision(ctx.TargetEntity, keyId);
			return rev == 0 ? 1u : rev;
		};
		source.SlotProvider = ctx =>
		{
			// Projector materializes ControlPlaneView into EntityViewKeys.ControlPlaneCommand.
			if (!collections.TryGet(ctx.TargetEntity, EntityViewKeys.ControlPlaneCommand, out EntityCollectionHandle handle))
			{
				return Array.Empty<EntityCommandPanelSlotView>();
			}

			var members = new Entity[8];
			int count = collections.CopyEntities(handle, 0, members);
			return
			[
				new EntityCommandPanelSlotView(
					0,
					31,
					0,
					EntityCommandSlotStateFlags.Base,
					0,
					0,
					0,
					"Train Infantry",
					count > 1
						? $"{count.ToString(System.Globalization.CultureInfo.InvariantCulture)} owners | ready"
						: "ready",
					"category.train")
			];
		};

		Harness harness = Harness.Create(
			source,
			installRoute: false,
			collections: collections,
			collectionKeys: keyRegistry,
			controlPlane: controlPlane);
		harness.InstallProfile(new CommandDeckProfileDefinition
		{
			Id = GlobalProfileId,
			DisplayMode = CommandDeckDisplayModeIds.Global,
			SourceKind = CommandDeckSourceKindIds.ControlPlaneView,
			SourceRef = "collection.command.source",
			CommandPanelSourceId = PanelSourceId,
			Topic = Topic
		});

		CommandDeckProfile profile = harness.Profiles.Require(GlobalProfileId);
		var binding = new CommandDeckBindingContext(p1, Entity.Null, p1, "collection.command.source", false);

		CommandDeckSnapshot before = harness.Projector.Project(profile, in binding, world);
		Assert.That(before.Entries, Has.Count.EqualTo(1));
		Assert.That(before.Entries[0].OwnerCount, Is.EqualTo(1));
		uint beforeRevision = before.Revision;

		relationships.EnsureLink(p1, p2, controlsTypeId);
		writer.ReplaceRouted(
			p1,
			keyId,
			stackalloc Entity[] { m01, m99 },
			EntityCollectionSourceKind.UiAcquisition,
			DomainRoutingUnresolvedPolicy.Reject);

		CommandDeckSnapshot expanded = harness.Projector.Project(profile, in binding, world);
		Assert.That(expanded.Entries[0].OwnerCount, Is.EqualTo(2));
		Assert.That(expanded.Revision, Is.Not.EqualTo(beforeRevision));

		relationships.RemoveLink(p1, p2, controlsTypeId);
		CommandDeckSnapshot shrunk = harness.Projector.Project(profile, in binding, world);
		Assert.That(shrunk.Entries[0].OwnerCount, Is.EqualTo(1));
		Assert.That(shrunk.Revision, Is.Not.EqualTo(expanded.Revision));
	}

	[Test]
	public void MissingRouteProfile_FailsFast_NoSilentFirstMemberFallback()
	{
		var source = new StubCommandPanelSource();
		Harness harness = Harness.Create(source, installRoute: true);

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			harness.InstallProfile(new CommandDeckProfileDefinition
			{
				Id = AggregateProfileId,
				DisplayMode = CommandDeckDisplayModeIds.AggregateFiltered,
				SourceKind = CommandDeckSourceKindIds.EntityCollection,
				SourceRef = "collection.command.source",
				CommandPanelSourceId = PanelSourceId,
				AggregationProfileId = AggregationByAbilityId,
				RouteProfileId = "dispatch.missing",
				Topic = Topic
			}))!;

		Assert.That(ex.Message, Does.Contain("dispatch.missing"));
	}

	[Test]
	public void MissingAggregationProfile_FailsFast()
	{
		var source = new StubCommandPanelSource();
		Harness harness = Harness.Create(source, installRoute: true);

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			harness.InstallProfile(new CommandDeckProfileDefinition
			{
				Id = AggregateProfileId,
				DisplayMode = CommandDeckDisplayModeIds.AggregateFiltered,
				SourceKind = CommandDeckSourceKindIds.EntityCollection,
				SourceRef = "collection.command.source",
				CommandPanelSourceId = PanelSourceId,
				AggregationProfileId = "aggregation.missing",
				RouteProfileId = RouteNearestId,
				Topic = Topic
			}))!;

		Assert.That(ex.Message, Does.Contain("aggregation.missing"));
	}

	[Test]
	public void DataPlaneProducer_EmitsRevisionModeAndEntries_ForManifestTopic()
	{
		using var world = World.Create();
		Entity player = world.Create();
		var source = new StubCommandPanelSource();
		source.SetSlots(
			new EntityCommandPanelSlotView(0, 51, 0, EntityCommandSlotStateFlags.Base, 0, 0, 0, "Research", "", "category.research"));
		Harness harness = Harness.Create(source, installRoute: false);
		harness.InstallProfile(new CommandDeckProfileDefinition
		{
			Id = GlobalProfileId,
			DisplayMode = CommandDeckDisplayModeIds.Global,
			SourceKind = CommandDeckSourceKindIds.LocalPlayerRep,
			SourceRef = "collection.command.source",
			CommandPanelSourceId = PanelSourceId,
			Topic = Topic
		});

		CommandDeckProfile profile = harness.Profiles.Require(GlobalProfileId);
		var producer = new CommandDeckWebUiTopicProducer(
			Topic,
			harness.Projector,
			profile,
			() => new CommandDeckBindingContext(player, Entity.Null, player, "collection.command.source", false));

		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(producer);
		Assert.That(runtime.IsTopicRegistered(Topic), Is.True);

		var context = new WebUiTopicContext("session", Topic, 1, JsonSerializer.SerializeToElement(new { }));
		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		JsonElement root = document.RootElement;
		Assert.That(root.GetProperty("profileId").GetString(), Is.EqualTo(GlobalProfileId));
		Assert.That(root.GetProperty("displayMode").GetString(), Is.EqualTo("global"));
		Assert.That(root.GetProperty("revision").GetUInt32(), Is.GreaterThan(0u));
		Assert.That(root.GetProperty("visible").GetBoolean(), Is.True);
		Assert.That(root.GetProperty("entries").GetArrayLength(), Is.EqualTo(1));
		Assert.That(root.GetProperty("entries")[0].GetProperty("ownerCount").GetInt32(), Is.EqualTo(1));
	}

	[Test]
	public void CommandDeckPanelDescriptor_RejectsAggregateWithoutRoute()
	{
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			new CommandDeckPanelDescriptor(
				"hud.command-deck.aggregate",
				CommandDeckPanelDescriptor.DisplayModeAggregateFiltered,
				"entityCollection",
				"collection.command.source",
				PanelSourceId,
				Topic,
				WebUiPanelKitSampleCatalog.CommandDeckAggregateProfileId,
				"layout.deck.grid",
				aggregationProfileId: AggregationByAbilityId))!;

		Assert.That(ex.Message, Does.Contain("routeProfileId"));
	}

	[Test]
	public void PanelKitManifest_AcceptsCommandDeckTopicSubscription()
	{
		using var runtime = new WebUiDataPlaneRuntime();
		runtime.RegisterTopic(new StubTopicProducer(WebUiPanelKitSampleCatalog.ResourceTopic));
		runtime.RegisterTopic(new StubTopicProducer(WebUiPanelKitSampleCatalog.CommandTopic));
		runtime.RegisterTopic(new StubTopicProducer(WebUiPanelKitSampleCatalog.ObjectiveTopic));
		runtime.RegisterTopic(new StubTopicProducer(Topic));
		WebUiPanelKitReferenceCatalog catalog = WebUiPanelKitSampleCatalog.Create(runtime.IsTopicRegistered);

		string json = JsonSerializer.Serialize(new
		{
			manifestId = "panel-kit.command-deck",
			hostOwnerId = "panel-kit.command-deck",
			panels = new[]
			{
				new
				{
					panelId = "hud.command-deck.global",
					panelType = "command-deck",
					surfaceRegionId = "region.bottom-center",
					surfaceSegment = "Overlay",
					surfacePriority = 20,
					anchor = "bottom-center",
					visibleConditionId = "condition.always",
					topic = Topic,
					profileId = WebUiPanelKitSampleCatalog.CommandDeckGlobalProfileId,
					layoutId = "layout.deck.grid",
					densityId = "density.comfortable",
					inputCapabilityId = "input.activate-slot"
				}
			}
		});

		WebUiPanelKitManifest manifest = WebUiPanelKitManifestLoader.LoadFromJson(json, catalog, "command-deck-test");
		Assert.That(manifest.DeclaredTopics, Is.EqualTo(new[] { Topic }));
	}

	[Test]
	public void CommandDeckSources_DoNotReferenceSelectionRuntimeAuthority()
	{
		string root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
		string[] paths =
		[
			Path.Combine(root, "src", "Core", "UI", "CommandDeck"),
			Path.Combine(root, "src", "Libraries", "Ludots.WebUI.DataPlane", "WebUiCoreTopicProducers.CommandDeck.cs"),
			Path.Combine(root, "src", "Libraries", "Ludots.WebUI.PanelKit", "CommandDeckPanelDescriptor.cs")
		];

		foreach (string path in paths)
		{
			IEnumerable<string> files = Directory.Exists(path)
				? Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
				: File.Exists(path) ? new[] { path } : Array.Empty<string>();

			foreach (string file in files)
			{
				foreach (string line in File.ReadLines(file))
				{
					string trimmed = line.TrimStart();
					if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
					    trimmed.StartsWith("///", StringComparison.Ordinal))
					{
						continue;
					}

					Assert.That(
						line,
						Does.Not.Contain("SelectionRuntime").And.Not.Contain("CurrentSelection"),
						$"CommandDeck code must not use SelectionRuntime/CurrentSelection: {file}");
				}
			}
		}
	}

	private sealed class Harness
	{
		public CommandDeckProfileRegistry Profiles = null!;
		public CommandDeckProjector Projector = null!;
		public AbilityAggregationProfileRegistry Aggregation = null!;
		public CastDispatchProfileRegistry Dispatch = null!;
		public StubCommandPanelSource Source = null!;

		public static Harness Create(
			StubCommandPanelSource source,
			bool installRoute,
			EntityCollectionStore? collections = null,
			StringIntRegistry? collectionKeys = null,
			ControlPlaneView? controlPlane = null,
			FilterProfileRegistry? filterProfiles = null)
		{
			var sources = new SourceRegistry();
			sources.Register(PanelSourceId, source);

			var aggregationIds = new StringIntRegistry(64, 1, 0, StringComparer.Ordinal);
			var aggregation = new AbilityAggregationProfileRegistry(aggregationIds);
			aggregation.Install(new AbilityAggregationProfilesConfig
			{
				Profiles = new List<AbilityAggregationProfileDefinition>
				{
					new() { Id = AggregationByAbilityId, GroupBy = "ability.id" }
				}
			});

			var dispatchIds = new StringIntRegistry(64, 1, 0, StringComparer.Ordinal);
			var advanceIds = new StringIntRegistry(64, 1, 0, StringComparer.Ordinal);
			var dispatch = new CastDispatchProfileRegistry(dispatchIds, advanceIds);
			if (installRoute)
			{
				dispatch.Install(new CastDispatchProfilesConfig
				{
					Profiles = new List<CastDispatchProfileDefinition>
					{
						new()
						{
							Id = RouteNearestId,
							Selector = new CastDispatchSelectorDefinition { Kind = "topN", N = 1 },
							Scorer = new CastDispatchScorerDefinition
							{
								Kind = "utility",
								Considerations = new List<string> { "distanceToTarget:invert" }
							},
							Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true }
						}
					}
				});
			}

			var profileIds = new StringIntRegistry(64, 1, 0, StringComparer.Ordinal);
			var profiles = new CommandDeckProfileRegistry(
				profileIds,
				aggregation,
				installRoute ? dispatch : null,
				filterProfiles: filterProfiles);

			var routeResolver = installRoute ? new CommandDeckRouteResolver(dispatch) : null;
			var projector = new CommandDeckProjector(
				sources,
				collections: collections,
				controlPlane: controlPlane,
				collectionKeys: collectionKeys,
				routeResolver: routeResolver,
				filterProfiles: filterProfiles);

			return new Harness
			{
				Profiles = profiles,
				Projector = projector,
				Aggregation = aggregation,
				Dispatch = dispatch,
				Source = source
			};
		}

		public void InstallProfile(CommandDeckProfileDefinition definition)
		{
			Profiles.Install(new CommandDeckProfilesConfig
			{
				Profiles = new List<CommandDeckProfileDefinition> { definition }
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

	private sealed class StubCommandPanelSource : IEntityCommandPanelContextSource, IEntityCommandPanelAggregationMemberSource
	{
		private EntityCommandPanelSlotView[] _slots = Array.Empty<EntityCommandPanelSlotView>();
		private readonly Dictionary<int, EntityCommandPanelAggregationMember[]> _membersBySlot = new();
		public Entity LastTarget { get; private set; }
		public Func<EntityCommandPanelSourceContext, EntityCommandPanelSlotView[]>? SlotProvider { get; set; }
		public Func<EntityCommandPanelSourceContext, uint>? RevisionProvider { get; set; }

		public void SetSlots(params EntityCommandPanelSlotView[] slots)
		{
			_slots = slots;
		}

		public void SetAggregationMembers(int slotIndex, params EntityCommandPanelAggregationMember[] members)
		{
			_membersBySlot[slotIndex] = members;
		}

		public bool TryGetRevision(in EntityCommandPanelSourceContext context, out uint revision)
		{
			LastTarget = context.TargetEntity;
			revision = RevisionProvider?.Invoke(context) ?? 7u;
			return true;
		}

		public int GetGroupCount(in EntityCommandPanelSourceContext context)
		{
			EntityCommandPanelSlotView[] slots = ResolveSlots(in context);
			return slots.Length > 0 ? 1 : 0;
		}

		public bool TryGetGroup(in EntityCommandPanelSourceContext context, int groupIndex, out EntityCommandPanelGroupView group)
		{
			EntityCommandPanelSlotView[] slots = ResolveSlots(in context);
			group = new EntityCommandPanelGroupView(0, "Commands", (byte)slots.Length);
			return groupIndex == 0 && slots.Length > 0;
		}

		public int CopySlots(in EntityCommandPanelSourceContext context, int groupIndex, Span<EntityCommandPanelSlotView> destination)
		{
			LastTarget = context.TargetEntity;
			if (groupIndex != 0)
			{
				return 0;
			}

			EntityCommandPanelSlotView[] slots = ResolveSlots(in context);
			int written = Math.Min(destination.Length, slots.Length);
			for (int i = 0; i < written; i++)
			{
				destination[i] = slots[i];
			}

			return written;
		}

		public int CopyAggregationMembers(
			in EntityCommandPanelSourceContext context,
			int groupIndex,
			int slotIndex,
			Span<EntityCommandPanelAggregationMember> destination)
		{
			if (groupIndex != 0 || !_membersBySlot.TryGetValue(slotIndex, out EntityCommandPanelAggregationMember[]? members))
			{
				return 0;
			}

			int written = Math.Min(destination.Length, members.Length);
			for (int i = 0; i < written; i++)
			{
				destination[i] = members[i];
			}

			return written;
		}

		private EntityCommandPanelSlotView[] ResolveSlots(in EntityCommandPanelSourceContext context)
		{
			return SlotProvider?.Invoke(context) ?? _slots;
		}

		public bool TryGetRevision(Entity target, out uint revision)
		{
			revision = 0;
			return false;
		}

		public int GetGroupCount(Entity target) => 0;

		public bool TryGetGroup(Entity target, int groupIndex, out EntityCommandPanelGroupView group)
		{
			group = default;
			return false;
		}

		public int CopySlots(Entity target, int groupIndex, Span<EntityCommandPanelSlotView> destination) => 0;
	}

	private sealed class StubTopicProducer : IWebUiTopicProducer
	{
		public StubTopicProducer(string topic) => Topic = topic;
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
}
