using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Hosting
{
    public readonly record struct NetworkReadinessSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        public NetworkReadinessSnapshot(
            NetworkProcessRole processRole,
            bool runtimeReady,
            bool sessionEstablished,
            ulong sessionEpoch,
            int replicatedMirrorCount,
            int renderableMirrorCount,
            int connectedSeatCount)
        {
            if (processRole is not (NetworkProcessRole.AuthoritativeServer or NetworkProcessRole.ReplicatedClient))
            {
                throw new ArgumentOutOfRangeException(nameof(processRole));
            }
            if (replicatedMirrorCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(replicatedMirrorCount));
            }
            if ((uint)renderableMirrorCount > (uint)replicatedMirrorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(renderableMirrorCount));
            }
            if (connectedSeatCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectedSeatCount));
            }
            if ((!runtimeReady &&
                 (sessionEstablished || sessionEpoch != 0 || replicatedMirrorCount != 0 || renderableMirrorCount != 0 || connectedSeatCount != 0)) ||
                (processRole == NetworkProcessRole.AuthoritativeServer &&
                 (!runtimeReady || sessionEpoch == 0 || sessionEstablished || replicatedMirrorCount != 0 || renderableMirrorCount != 0)) ||
                (processRole == NetworkProcessRole.ReplicatedClient && sessionEstablished && sessionEpoch == 0))
            {
                throw new ArgumentException("Network readiness fields contradict the declared process state.");
            }

            SchemaVersion = CurrentSchemaVersion;
            ProcessRole = processRole;
            RuntimeReady = runtimeReady;
            SessionEstablished = sessionEstablished;
            SessionEpoch = sessionEpoch;
            ReplicatedMirrorCount = replicatedMirrorCount;
            RenderableMirrorCount = renderableMirrorCount;
            ConnectedSeatCount = connectedSeatCount;
        }

        public int SchemaVersion { get; }
        public NetworkProcessRole ProcessRole { get; }
        public bool RuntimeReady { get; }
        public bool SessionEstablished { get; }
        public ulong SessionEpoch { get; }
        public int ReplicatedMirrorCount { get; }
        public int RenderableMirrorCount { get; }
        public int ConnectedSeatCount { get; }
    }

    internal interface INetworkReadinessArtifactSink : IDisposable
    {
        void Publish(in NetworkReadinessSnapshot snapshot);
    }

    internal sealed class AtomicJsonNetworkReadinessArtifact : INetworkReadinessArtifactSink
    {
        private static readonly TimeSpan ReplaceTimeout = TimeSpan.FromMilliseconds(250);
        private readonly string _artifactPath;
        private readonly string _temporaryPath;
        private bool _disposed;

        public AtomicJsonNetworkReadinessArtifact(string artifactPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
            if (!Path.IsPathFullyQualified(artifactPath))
            {
                throw new ArgumentException("Network readiness artifact path must be fully resolved.", nameof(artifactPath));
            }

            _artifactPath = Path.GetFullPath(artifactPath);
            _temporaryPath = _artifactPath + ".tmp";
            string directory = Path.GetDirectoryName(_artifactPath) ??
                throw new ArgumentException("Network readiness artifact path has no parent directory.", nameof(artifactPath));
            Directory.CreateDirectory(directory);
            DeleteArtifacts();
        }

        public void Publish(in NetworkReadinessSnapshot snapshot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                using (var stream = new FileStream(
                    _temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                {
                    using var json = new Utf8JsonWriter(stream);
                    json.WriteStartObject();
                    json.WriteNumber("schemaVersion", snapshot.SchemaVersion);
                    json.WriteString("processRole", ResolveRoleName(snapshot.ProcessRole));
                    json.WriteBoolean("runtimeReady", snapshot.RuntimeReady);
                    json.WriteBoolean("sessionEstablished", snapshot.SessionEstablished);
                    json.WriteNumber("sessionEpoch", snapshot.SessionEpoch);
                    json.WriteNumber("connectedSeatCount", snapshot.ConnectedSeatCount);
                    json.WriteNumber("replicatedMirrorCount", snapshot.ReplicatedMirrorCount);
                    json.WriteNumber("renderableMirrorCount", snapshot.RenderableMirrorCount);
                    json.WriteString("updatedAtUtc", DateTime.UtcNow);
                    json.WriteEndObject();
                    json.Flush();
                    stream.Flush(flushToDisk: true);
                }

                ReplaceArtifactWithinDeadline(_temporaryPath, _artifactPath);
            }
            catch (Exception publishFailure)
            {
                Exception? cleanupFailure = TryDelete(_temporaryPath);
                if (cleanupFailure != null)
                {
                    throw new AggregateException(publishFailure, cleanupFailure);
                }
                throw;
            }
        }

        private static void ReplaceArtifactWithinDeadline(string temporaryPath, string artifactPath)
        {
            var stopwatch = Stopwatch.StartNew();
            Exception? firstFailure = null;
            int attemptCount = 0;
            while (true)
            {
                attemptCount++;
                try
                {
                    File.Move(temporaryPath, artifactPath, overwrite: true);
                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    firstFailure ??= exception;
                    if (stopwatch.Elapsed >= ReplaceTimeout)
                    {
                        throw new IOException(
                            $"Failed to atomically replace network readiness artifact '{artifactPath}' " +
                            $"within {ReplaceTimeout.TotalMilliseconds:F0} ms after {attemptCount} attempts.",
                            new AggregateException(firstFailure, exception));
                    }

                    Thread.Sleep(1);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DeleteArtifacts();
        }

        private void DeleteArtifacts()
        {
            Exception? artifactFailure = TryDelete(_artifactPath);
            Exception? temporaryFailure = TryDelete(_temporaryPath);
            if (artifactFailure != null && temporaryFailure != null)
            {
                throw new AggregateException(artifactFailure, temporaryFailure);
            }
            if (artifactFailure != null)
            {
                throw artifactFailure;
            }
            if (temporaryFailure != null)
            {
                throw temporaryFailure;
            }
        }

        private static Exception? TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static string ResolveRoleName(NetworkProcessRole role) => role switch
        {
            NetworkProcessRole.AuthoritativeServer => "authoritativeServer",
            NetworkProcessRole.ReplicatedClient => "replicatedClient",
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
    }

    public sealed class NetworkReadinessArtifactRuntime :
        INetworkRuntimePort,
        IReplicatedClientRuntimeStatus,
        IPresentationInterpolationSource
    {
        private static readonly QueryDescription MirrorQuery = new QueryDescription()
            .WithAll<ReplicationMirrorIdentity>();

        private static readonly QueryDescription RenderableMirrorCandidateQuery = new QueryDescription()
            .WithAll<ReplicationMirrorIdentity, WorldPositionCm, PreviousWorldPositionCm, VisualTransform,
                PresentationOwnerHasPresenterPayload>()
            .WithNone<SuspendedTag, PresentationDestroyPending>();

        private readonly World _world;
        private readonly INetworkRuntimePort _inner;
        private readonly NetworkRuntimeStateObserver _observer;
        private readonly INetworkReadinessArtifactSink _artifact;
        private NetworkReadinessSnapshot _lastPublishedClientSnapshot;
        private int _lastServerConnectedSeatCount = -1;
        private ulong _lastServerSessionEpoch;
        private bool _hasPublishedClientSnapshot;
        private bool _activated;
        private bool _disposed;

        public NetworkReadinessArtifactRuntime(
            World world,
            INetworkRuntimePort inner,
            NetworkRuntimeStateObserver observer,
            string artifactPath)
            : this(world, inner, observer, new AtomicJsonNetworkReadinessArtifact(artifactPath))
        {
        }

        internal NetworkReadinessArtifactRuntime(
            World world,
            INetworkRuntimePort inner,
            NetworkRuntimeStateObserver observer,
            INetworkReadinessArtifactSink artifact)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
            _artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
            if (_inner.Role == NetworkProcessRole.Standalone)
            {
                throw new ArgumentOutOfRangeException(nameof(inner));
            }
            if (_inner.Role == NetworkProcessRole.ReplicatedClient &&
                (_inner is not IReplicatedClientRuntimeStatus || _inner is not IPresentationInterpolationSource))
            {
                throw new ArgumentException(
                    "Replicated-client readiness requires client status and interpolation contracts.",
                    nameof(inner));
            }
        }

        public NetworkProcessRole Role => _inner.Role;
        public ReplicatedClientConnectionState ConnectionState => GetClientStatus().ConnectionState;
        public bool HasEstablishedSession => GetClientStatus().HasEstablishedSession;
        public bool IsAwaitingFullSnapshot => GetClientStatus().IsAwaitingFullSnapshot;
        public bool IsFaulted => GetClientStatus().IsFaulted;
        public uint LastCommittedTick => GetClientStatus().LastCommittedTick;
        public float ReconnectWindowRemainingSeconds => GetClientStatus().ReconnectWindowRemainingSeconds;
        public int RoundTripTimeMilliseconds => GetClientStatus().RoundTripTimeMilliseconds;
        public float InterpolationAlpha => GetClientInterpolation().InterpolationAlpha;

        public void Activate()
        {
            ThrowIfDisposed();
            if (_activated)
            {
                return;
            }

            _inner.Activate();
            _activated = true;
        }

        public void PumpTransport()
        {
            EnsureActivated();
            _inner.PumpTransport();
            if (Role == NetworkProcessRole.AuthoritativeServer)
            {
                ObserveServer();
            }
            else
            {
                ObserveClient();
            }
        }

        public void BeforeAuthoritativeTick(uint executingTick)
        {
            EnsureActivated();
            _inner.BeforeAuthoritativeTick(executingTick);
        }

        public void AfterAuthoritativeCommit(uint committedTick)
        {
            EnsureActivated();
            _inner.AfterAuthoritativeCommit(committedTick);
        }

        public void PumpReplicatedClient(float frameDeltaTime)
        {
            EnsureActivated();
            _inner.PumpReplicatedClient(frameDeltaTime);
            ObserveClient();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            List<Exception>? failures = null;
            try
            {
                _artifact.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>(2)).Add(exception);
            }

            try
            {
                _inner.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>(2)).Add(exception);
            }

            if (failures is { Count: 1 })
            {
                throw failures[0];
            }
            if (failures is { Count: > 1 })
            {
                throw new AggregateException(failures);
            }
        }

        private void ObserveServer()
        {
            if (!_observer.HasRoomSnapshot)
            {
                return;
            }

            ulong sessionEpoch = _observer.LastRoomSnapshot.SessionEpoch.Value;
            int connectedSeatCount = _observer.ConnectedSeatCount;
            if (_lastServerConnectedSeatCount == connectedSeatCount &&
                _lastServerSessionEpoch == sessionEpoch)
            {
                return;
            }

            var snapshot = new NetworkReadinessSnapshot(
                Role,
                runtimeReady: true,
                sessionEstablished: false,
                sessionEpoch,
                replicatedMirrorCount: 0,
                renderableMirrorCount: 0,
                connectedSeatCount);
            _artifact.Publish(in snapshot);
            _lastServerConnectedSeatCount = connectedSeatCount;
            _lastServerSessionEpoch = sessionEpoch;
        }

        private void ObserveClient()
        {
            IReplicatedClientRuntimeStatus status = GetClientStatus();
            ulong sessionEpoch = _observer.LastClientHandshake.Accepted
                ? _observer.LastClientHandshake.SessionEpoch.Value
                : 0;
            var signal = new ClientObservationSignal(
                status.ConnectionState,
                status.HasEstablishedSession,
                status.IsAwaitingFullSnapshot,
                status.LastCommittedTick,
                sessionEpoch,
                _observer.ConnectedSeatCount);
            bool sessionEstablished = signal.ConnectionState == ReplicatedClientConnectionState.Connected &&
                signal.HasEstablishedSession &&
                !signal.IsAwaitingFullSnapshot;
            CountClientMirrors(out int mirrorCount, out int renderableMirrorCount);
            var snapshot = new NetworkReadinessSnapshot(
                Role,
                runtimeReady: true,
                sessionEstablished,
                signal.SessionEpoch,
                mirrorCount,
                renderableMirrorCount,
                signal.ConnectedSeatCount);
            if (_hasPublishedClientSnapshot && snapshot == _lastPublishedClientSnapshot)
            {
                return;
            }

            _artifact.Publish(in snapshot);
            _lastPublishedClientSnapshot = snapshot;
            _hasPublishedClientSnapshot = true;
        }

        private void CountClientMirrors(out int mirrorCount, out int renderableMirrorCount)
        {
            mirrorCount = 0;
            foreach (ref readonly Chunk chunk in _world.Query(in MirrorQuery))
            {
                mirrorCount = checked(mirrorCount + chunk.Count);
            }

            renderableMirrorCount = 0;
            foreach (ref readonly Chunk chunk in _world.Query(in RenderableMirrorCandidateQuery))
            {
                Span<PresentationOwnerHasPresenterPayload> payloads =
                    chunk.GetSpan<PresentationOwnerHasPresenterPayload>();
                foreach (int index in chunk)
                {
                    if (payloads[index].Count > 0 && payloads[index].RootCount > 0)
                    {
                        renderableMirrorCount = checked(renderableMirrorCount + 1);
                    }
                }
            }
        }

        private IReplicatedClientRuntimeStatus GetClientStatus()
        {
            if (Role != NetworkProcessRole.ReplicatedClient)
            {
                throw new InvalidOperationException("Only replicated clients expose client readiness status.");
            }

            return (IReplicatedClientRuntimeStatus)_inner;
        }

        private IPresentationInterpolationSource GetClientInterpolation()
        {
            if (Role != NetworkProcessRole.ReplicatedClient)
            {
                throw new InvalidOperationException("Only replicated clients expose presentation interpolation.");
            }

            return (IPresentationInterpolationSource)_inner;
        }

        private void EnsureActivated()
        {
            ThrowIfDisposed();
            if (!_activated)
            {
                throw new InvalidOperationException(
                    "Network readiness cannot be observed before the network runtime is activated.");
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

        private readonly record struct ClientObservationSignal(
            ReplicatedClientConnectionState ConnectionState,
            bool HasEstablishedSession,
            bool IsAwaitingFullSnapshot,
            uint LastCommittedTick,
            ulong SessionEpoch,
            int ConnectedSeatCount);
    }
}
