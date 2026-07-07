using System.Text.Json;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Registry;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;
using ControlPlaneProjectionShowcaseMod;
using ControlPlaneProjectionShowcaseMod.DataPlane;
using ControlPlaneProjectionShowcaseMod.Runtime;

namespace Ludots.Tests.WebUiDataPlane;

/// <summary>
/// Contract tests for the control plane projection showcase dataplane (RFC-0065 SHOW-2): topic snapshot
/// shape, the tag-only toggleProxy command, router/permission wiring, and the not-ready behavior. The
/// scenario state is constructed directly (no engine) with the minimal runtime it needs to be Ready.
/// </summary>
[TestFixture]
public sealed class ControlPlaneProjectionDataPlaneTests
{
	private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

	[Test]
	public void TryCreateSnapshot_EmitsProxyStateProjectionsAndRevision()
	{
		using var harness = Harness.Create(bindRuntime: true);
		var context = new WebUiTopicContext("session-a", harness.Producer.Topic, 7, JsonSerializer.SerializeToElement(new { }));

		Assert.That(harness.Producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);

		Assert.That(packet.Topic, Is.EqualTo(ControlPlaneProjectionShowcaseIds.WebUiTopic));
		Assert.That(packet.Kind, Is.EqualTo(WebUiPacketKind.Snapshot));
		Assert.That(packet.Delivery, Is.EqualTo(WebUiDeliverySemantics.LatestWins));
		Assert.That(packet.ContentType, Is.EqualTo("application/json"));

		using JsonDocument document = JsonDocument.Parse(packet.Payload);
		JsonElement root = document.RootElement;
		Assert.That(root.GetProperty("proxyActive").GetBoolean(), Is.False);
		Assert.That(root.GetProperty("revision").GetUInt32(), Is.EqualTo(harness.View.ComputeRevision(harness.State.P1Rep, harness.State.CommandSourceKeyId)));

		JsonElement owned = root.GetProperty("ownedMembers");
		Assert.That(owned.GetArrayLength(), Is.EqualTo(1));
		Assert.That(owned[0].GetProperty("entityId").GetInt32(), Is.EqualTo(harness.OwnedUnit.Id));
		Assert.That(owned[0].GetProperty("version").GetInt32(), Is.EqualTo(harness.OwnedUnit.Version));
		Assert.That(owned[0].GetProperty("name").GetString(), Is.EqualTo("Owned Unit"));

		JsonElement proxied = root.GetProperty("proxiedMembers");
		Assert.That(proxied.GetArrayLength(), Is.EqualTo(1));
		Assert.That(proxied[0].GetProperty("entityId").GetInt32(), Is.EqualTo(harness.ProxiedUnit.Id));
		Assert.That(proxied[0].GetProperty("name").GetString(), Is.EqualTo("Proxied Unit"));

		JsonElement p2Domain = root.GetProperty("p2DomainMembers");
		Assert.That(p2Domain.GetArrayLength(), Is.EqualTo(1));
		Assert.That(p2Domain[0].GetProperty("entityId").GetInt32(), Is.EqualTo(harness.ProxiedUnit.Id));

		// The tag-only toggle is reflected in the next snapshot.
		Assert.That(harness.Producer.ApplyCommand(ToggleRequest()).Success, Is.True);
		Assert.That(harness.Producer.TryCreateSnapshot(in context, out WebUiOutboundPacket toggled), Is.True);
		using JsonDocument toggledDocument = JsonDocument.Parse(toggled.Payload);
		Assert.That(toggledDocument.RootElement.GetProperty("proxyActive").GetBoolean(), Is.True);
	}

	[Test]
	public void ApplyCommand_ToggleProxy_FlipsTriggerTagOnly_AndRejectsUnknownCommands()
	{
		using var harness = Harness.Create(bindRuntime: true);
		bool hadControlsEdge = harness.HasAnyControlsEdge();

		WebUiCommandResult on = harness.Producer.ApplyCommand(ToggleRequest());
		Assert.That(on.Success, Is.True);
		Assert.That(harness.State.ProxyActive, Is.True, "toggleProxy must flip ProxyActive on.");
		Assert.That(harness.HasOfflineTag(harness.State.P2Rep), Is.True, "toggleProxy must add the trigger tag on P2Rep.");
		Assert.That(harness.HasAnyControlsEdge(), Is.EqualTo(hadControlsEdge), "The dataplane command is tag-only; Controls edges may not be written directly.");

		WebUiCommandResult off = harness.Producer.ApplyCommand(ToggleRequest());
		Assert.That(off.Success, Is.True);
		Assert.That(harness.State.ProxyActive, Is.False, "toggleProxy must flip ProxyActive off.");
		Assert.That(harness.HasOfflineTag(harness.State.P2Rep), Is.False, "toggleProxy must remove the trigger tag from P2Rep.");
		Assert.That(harness.HasAnyControlsEdge(), Is.EqualTo(hadControlsEdge), "The dataplane command must not revoke or create Controls edges.");

		WebUiCommandResult unknown = harness.Producer.ApplyCommand(CommandRequest("teleportEverything"));
		Assert.That(unknown.Success, Is.False);
		Assert.That(unknown.ErrorCode, Is.EqualTo("unknown_command"));
	}

	[Test]
	public async Task CommandRouter_DispatchesToggleProxy_AndPermissionValidatorRejectsUnknownCommands()
	{
		using var harness = Harness.Create(bindRuntime: true);
		var router = new WebUiCommandRouter(
			new ControlPlaneProjectionGenerationResolver(),
			new ControlPlaneProjectionPermissionValidator());
		router.Register(ControlPlaneProjectionShowcaseIds.ToggleProxyCommand, new ControlPlaneProjectionCommandHandler(harness.Producer));

		WebUiOutboundPacket ack = await router.HandleAsync(CommandPacket(ToggleRequest(clientSeq: 11)));
		Assert.That(ack.Kind, Is.EqualTo(WebUiPacketKind.CommandAck));
		Assert.That(ack.ClientSeq, Is.EqualTo(11));
		Assert.That(harness.State.ProxyActive, Is.True, "Router dispatch must reach the toggleProxy handler.");

		var validator = new ControlPlaneProjectionPermissionValidator();
		Assert.That(validator.CanUse(ToggleRequest(), out string allowedError), Is.True);
		Assert.That(allowedError, Is.Empty);
		Assert.That(validator.CanUse(CommandRequest("teleportEverything"), out string deniedError), Is.False);
		Assert.That(deniedError, Is.Not.Empty);
	}

	[Test]
	public void NotReadyState_SnapshotReturnsFalse_AndCommandFailsWithScenarioNotReady()
	{
		using var harness = Harness.Create(bindRuntime: false);
		var context = new WebUiTopicContext("session-a", harness.Producer.Topic, 7, JsonSerializer.SerializeToElement(new { }));

		Assert.That(harness.Producer.TryCreateSnapshot(in context, out _), Is.False);

		WebUiCommandResult result = harness.Producer.ApplyCommand(ToggleRequest());
		Assert.That(result.Success, Is.False);
		Assert.That(result.ErrorCode, Is.EqualTo("scenario_not_ready"));
	}

	private static WebUiCommandRequest ToggleRequest(long clientSeq = 1)
	{
		return CommandRequest(ControlPlaneProjectionShowcaseIds.ToggleProxyCommand, clientSeq);
	}

	private static WebUiCommandRequest CommandRequest(string name, long clientSeq = 1)
	{
		return new WebUiCommandRequest(name, clientSeq, Array.Empty<WebUiEntityRef>(), JsonSerializer.SerializeToElement(new { }));
	}

	private static WebUiInboundPacket CommandPacket(WebUiCommandRequest request)
	{
		return new WebUiInboundPacket(
			"session-a",
			ControlPlaneProjectionShowcaseIds.WebUiTopic,
			WebUiPacketKind.Command,
			WebUiDeliverySemantics.ReliableOrdered,
			JsonSerializer.SerializeToUtf8Bytes(request, WebJson),
			"application/json",
			RequestId: 3,
			ClientSeq: request.ClientSeq);
	}

	private sealed class Harness : IDisposable
	{
		public World World = null!;
		public RelationshipRuntime Relationships = null!;
		public ControlPlaneView View = null!;
		public ControlPlaneProjectionScenarioState State = null!;
		public ControlPlaneProjectionDataPlane Producer = null!;
		public Entity OwnedUnit;
		public Entity ProxiedUnit;
		public int ControlsTypeId;

		public static Harness Create(bool bindRuntime)
		{
			World world = World.Create();
			var keys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
			var store = new EntityCollectionStore(keys, initialCollectionCapacity: 8, initialRowCapacity: 32);
			var types = new RelationshipTypeRegistry();
			var relationships = new RelationshipRuntime(
				world,
				types,
				new RelationshipMetricRegistry(),
				new RelationshipFlagRegistry(),
				new RelationshipBandRegistry(),
				new RelationshipChangeBuffer(capacity: 8),
				new RelationshipReverseIndex(world));
			int ownsTypeId = types.Register(ControlPlaneProjectionShowcaseIds.OwnsRelationshipType);
			int controlsTypeId = types.Register(ControlPlaneProjectionShowcaseIds.ControlsRelationshipType);
			var domains = new ControlDomainQuery(world, relationships, new OwnershipResolver(relationships, ownsTypeId), ownsTypeId, controlsTypeId);
			var view = new ControlPlaneView(store, domains);

			var state = new ControlPlaneProjectionScenarioState
			{
				P1Rep = world.Create(new PlayerIdentity { PlayerId = 1 }, new GameplayTagContainer(), new TagCountContainer()),
				P2Rep = world.Create(new PlayerIdentity { PlayerId = 2 }, new GameplayTagContainer(), new TagCountContainer()),
				OfflineTagId = TagRegistry.Register(ControlPlaneProjectionShowcaseIds.OfflineTag),
				CommandSourceKeyId = keys.Register(EntityCollectionKeys.CommandSource),
				OwnedProjectionKeyId = keys.Register(ControlPlaneProjectionShowcaseIds.OwnedProjectionCollectionKey),
				ProxiedProjectionKeyId = keys.Register(ControlPlaneProjectionShowcaseIds.ProxiedProjectionCollectionKey),
			};

			Entity ownedUnit = world.Create(new Name { Value = "Owned Unit" });
			Entity proxiedUnit = world.Create(new Name { Value = "Proxied Unit" });
			relationships.EnsureLink(state.P1Rep, ownedUnit, ownsTypeId);
			relationships.EnsureLink(state.P2Rep, proxiedUnit, ownsTypeId);
			relationships.EnsureLink(state.P1Rep, state.P2Rep, controlsTypeId);
			store.Replace(
				state.P1Rep,
				EntityCollectionDescriptor.Create(
					EntityCollectionKeys.CommandSource,
					EntityCollectionSourceKind.SelectionView,
					EntityCollectionRoleKind.CommandSource,
					state.P1Rep),
				new[] { ownedUnit });
			store.Replace(
				state.P2Rep,
				EntityCollectionDescriptor.Create(
					EntityCollectionKeys.CommandSource,
					EntityCollectionSourceKind.SelectionView,
					EntityCollectionRoleKind.CommandSource,
					state.P2Rep),
				new[] { proxiedUnit });

			if (bindRuntime)
			{
				state.BindRuntime(world, new TagOps());
			}

			return new Harness
			{
				World = world,
				Relationships = relationships,
				View = view,
				State = state,
				Producer = new ControlPlaneProjectionDataPlane(world, store, view, state),
				OwnedUnit = ownedUnit,
				ProxiedUnit = proxiedUnit,
				ControlsTypeId = controlsTypeId,
			};
		}

		public bool HasOfflineTag(Entity entity)
		{
			ref GameplayTagContainer tags = ref World.Get<GameplayTagContainer>(entity);
			return tags.HasTag(State.OfflineTagId);
		}

		public bool HasAnyControlsEdge()
		{
			return Relationships.HasLink(State.P1Rep, State.P2Rep, ControlsTypeId) ||
				Relationships.HasLink(State.P2Rep, State.P1Rep, ControlsTypeId);
		}

		public void Dispose()
		{
			World.Dispose();
		}
	}
}
