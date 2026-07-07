using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.WebUI.DataPlane;
using ControlPlaneProjectionShowcaseMod.Runtime;

namespace ControlPlaneProjectionShowcaseMod.DataPlane
{
    /// <summary>
    /// CEF-ready WebUI dataplane contract for the control plane projection showcase. Publishes the
    /// topic <c>ludots.showcase.control_plane.state</c> as a JSON snapshot and handles the
    /// <c>toggleProxy</c> command. The contract layer only depends on the entity collection store and
    /// the shared scenario state; activation (surface + transport) is capability-gated elsewhere.
    /// </summary>
    public sealed class ControlPlaneProjectionDataPlane : IWebUiTopicProducer
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly World _world;
        private readonly EntityCollectionStore _store;
        private readonly ControlPlaneView _controlPlaneView;
        private readonly ControlPlaneProjectionScenarioState _state;
        private Entity[] _memberScratch = new Entity[64];
        private Entity[] _domainScratch = new Entity[64];
        private Entity[] _ownedScratch = new Entity[64];
        private Entity[] _proxiedScratch = new Entity[64];

        public ControlPlaneProjectionDataPlane(
            World world,
            EntityCollectionStore store,
            ControlPlaneView controlPlaneView,
            ControlPlaneProjectionScenarioState state)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _controlPlaneView = controlPlaneView ?? throw new ArgumentNullException(nameof(controlPlaneView));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public string Topic => ControlPlaneProjectionShowcaseIds.WebUiTopic;

        public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
        {
            packet = default!;
            if (!_state.Ready)
            {
                return false;
            }

            ControlPlaneProjectionSnapshot snapshot = _state.RefereeReady
                ? CreateRefereeSnapshot()
                : CreateP1Snapshot();
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            packet = new WebUiOutboundPacket(
                context.SessionId,
                Topic,
                WebUiPacketKind.Snapshot,
                WebUiDeliverySemantics.LatestWins,
                payload,
                "application/json",
                context.RequestId);
            return true;
        }

        private ControlPlaneProjectionSnapshot CreateP1Snapshot()
        {
            int viewCount = CopyControlPlaneView(_state.P1Rep);
            return new ControlPlaneProjectionSnapshot(
                "control-plane",
                "Command Grant",
                _state.ProxyActive,
                CopyMembers(_ownedScratch.AsSpan(0, CountDomainMatches(viewCount, _state.P1Rep, matchesOwnDomain: true))),
                CopyMembers(_proxiedScratch.AsSpan(0, CountDomainMatches(viewCount, _state.P1Rep, matchesOwnDomain: false))),
                CopyMembers(_state.P2Rep, _state.CommandSourceKeyId),
                _controlPlaneView.ComputeRevision(_state.P1Rep, _state.CommandSourceKeyId),
                "My squad starts with one owned unit and one temporary ally grant",
                _state.ProxyActive ? "The ally command grant is on" : "The ally command grant is off",
                _state.ProxyActive
                    ? "Owned units and temporary allies are commandable together"
                    : "Only my owned units are commandable",
                "My Units",
                "Always under my command",
                "Temporary Allies",
                "Granted by the scenario",
                "Ally Roster");
        }

        private ControlPlaneProjectionSnapshot CreateRefereeSnapshot()
        {
            int viewCount = CopyControlPlaneView(_state.RefereeRep);
            int ownedCount = CountDomainMatches(viewCount, _state.RefereeRep, matchesOwnDomain: true);
            int proxiedCount = CountDomainMatches(viewCount, _state.RefereeRep, matchesOwnDomain: false);
            return new ControlPlaneProjectionSnapshot(
                "referee",
                "Referee Command",
                _state.RefereeP2GrantActive,
                CopyMembers(_ownedScratch.AsSpan(0, ownedCount)),
                CopyMembers(_proxiedScratch.AsSpan(0, proxiedCount)),
                CopyMembers(_state.ForeignRep, _state.CommandSourceKeyId),
                _controlPlaneView.ComputeRevision(_state.RefereeRep, _state.CommandSourceKeyId),
                "The referee can command marked units but outsiders stay excluded",
                _state.RefereeP2GrantActive ? "The second ally grant is active" : "The second ally grant was revoked",
                $"The referee sees {ownedCount} marked unit(s) and {proxiedCount} granted unit(s)",
                "Referee Units",
                "Marked for direct command",
                "Granted Allies",
                "Reachable through control grants",
                "Excluded Outsiders");
        }

        public WebUiCommandResult ApplyCommand(WebUiCommandRequest request)
        {
            if (!string.Equals(request.Name, ControlPlaneProjectionShowcaseIds.ToggleProxyCommand, StringComparison.Ordinal))
            {
                return WebUiCommandResult.Fail(
                    "unknown_command",
                    $"Unsupported control plane projection command '{request.Name}'.");
            }

            if (!_state.Ready)
            {
                return WebUiCommandResult.Fail(
                    "scenario_not_ready",
                    "Control plane projection scenario is not bootstrapped yet.");
            }

            _state.ToggleProxy();
            return WebUiCommandResult.Ok();
        }

        private ControlPlaneProjectionMemberView[] CopyMembers(Entity owner, int keyId)
        {
            if (!_store.TryGet(owner, keyId, out EntityCollectionHandle handle) ||
                !_store.TryGetView(handle, out EntityCollectionView view))
            {
                return Array.Empty<ControlPlaneProjectionMemberView>();
            }

            if (_memberScratch.Length < view.Count)
            {
                Array.Resize(ref _memberScratch, Math.Max(view.Count, _memberScratch.Length * 2));
            }

            int count = _store.CopyEntities(handle, 0, _memberScratch.AsSpan(0, view.Count));
            var members = new ControlPlaneProjectionMemberView[count];
            for (int i = 0; i < count; i++)
            {
                Entity entity = _memberScratch[i];
                string name = _world.IsAlive(entity) && _world.TryGet(entity, out Name resolvedName)
                    ? resolvedName.Value
                    : string.Empty;
                members[i] = new ControlPlaneProjectionMemberView(entity.Id, entity.WorldId, entity.Version, name);
            }

            return members;
        }

        private ControlPlaneProjectionMemberView[] CopyMembers(ReadOnlySpan<Entity> entities)
        {
            var members = new ControlPlaneProjectionMemberView[entities.Length];
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                string name = _world.IsAlive(entity) && _world.TryGet(entity, out Name resolvedName)
                    ? resolvedName.Value
                    : string.Empty;
                members[i] = new ControlPlaneProjectionMemberView(entity.Id, entity.WorldId, entity.Version, name);
            }

            return members;
        }

        private int CopyControlPlaneView(Entity viewer)
        {
            while (true)
            {
                int count = _controlPlaneView.CopyMembersWithDomain(
                    viewer,
                    _state.CommandSourceKeyId,
                    _memberScratch,
                    _domainScratch);
                if (count < _memberScratch.Length)
                {
                    return count;
                }

                int next = _memberScratch.Length * 2;
                Array.Resize(ref _memberScratch, next);
                Array.Resize(ref _domainScratch, next);
                Array.Resize(ref _ownedScratch, next);
                Array.Resize(ref _proxiedScratch, next);
            }
        }

        private int CountDomainMatches(int viewCount, Entity ownDomain, bool matchesOwnDomain)
        {
            int written = 0;
            for (int i = 0; i < viewCount; i++)
            {
                bool isOwnDomain = _domainScratch[i] == ownDomain;
                if (isOwnDomain == matchesOwnDomain)
                {
                    Entity entity = _memberScratch[i];
                    if (matchesOwnDomain)
                    {
                        _ownedScratch[written++] = entity;
                    }
                    else
                    {
                        _proxiedScratch[written++] = entity;
                    }
                }
            }

            return written;
        }
    }

    public sealed record ControlPlaneProjectionSnapshot(
        string Mode,
        string Title,
        bool ProxyActive,
        ControlPlaneProjectionMemberView[] OwnedMembers,
        ControlPlaneProjectionMemberView[] ProxiedMembers,
        ControlPlaneProjectionMemberView[] P2DomainMembers,
        uint Revision,
        string GivenText,
        string WhenText,
        string ThenText,
        string OwnedTitle,
        string OwnedSubtitle,
        string ProxyTitle,
        string ProxySubtitle,
        string DomainTitle);

    public sealed record ControlPlaneProjectionMemberView(int EntityId, int WorldId, int Version, string Name);

    public sealed class ControlPlaneProjectionCommandHandler : IWebUiCommandHandler
    {
        private readonly ControlPlaneProjectionDataPlane _producer;

        public ControlPlaneProjectionCommandHandler(ControlPlaneProjectionDataPlane producer)
        {
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        }

        public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_producer.ApplyCommand(request));
        }
    }

    public sealed class ControlPlaneProjectionGenerationResolver : IWebUiEntityGenerationResolver
    {
        public bool IsCurrent(WebUiEntityRef entityRef)
        {
            return entityRef.StableId <= 0 && entityRef.Generation <= 0;
        }
    }

    public sealed class ControlPlaneProjectionPermissionValidator : IWebUiCommandPermissionValidator
    {
        public bool CanUse(WebUiCommandRequest request, out string error)
        {
            if (string.Equals(request.Name, ControlPlaneProjectionShowcaseIds.ToggleProxyCommand, StringComparison.Ordinal))
            {
                error = string.Empty;
                return true;
            }

            error = $"Command '{request.Name}' is not allowed in ControlPlaneProjectionShowcaseMod.";
            return false;
        }
    }
}
