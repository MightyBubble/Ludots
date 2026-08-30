using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;

namespace Ludots.Adapter.Raylib;

public sealed record RaylibBackendSceneDescriptor(
    string Id,
    bool Enabled,
    string[] MapIds,
    string[] SourceUris,
    Vector3 Position,
    Quaternion Rotation,
    Vector3 Scale)
{
    public bool AppliesTo(string mapId)
    {
        for (int i = 0; i < MapIds.Length; i++)
        {
            if (string.Equals(MapIds[i], mapId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

public static class RaylibBackendSceneCatalog
{
    public const string DefaultRelativePath = "Presentation/raylib_scenes.json";

    private static readonly string[] Fields =
    {
        "id", "enabled", "mapIds", "sourceUris", "position", "rotation", "scale",
    };

    public static IReadOnlyList<RaylibBackendSceneDescriptor> Parse(
        IReadOnlyList<MergedConfigEntry> merged)
    {
        if (merged == null)
        {
            throw new ArgumentNullException(nameof(merged));
        }

        var result = new List<RaylibBackendSceneDescriptor>(merged.Count);
        for (int i = 0; i < merged.Count; i++)
        {
            MergedConfigEntry entry = merged[i];
            JsonObject obj = entry.Node ?? throw new InvalidOperationException(
                $"{DefaultRelativePath} entry '{entry.Id}' must merge to a JSON object.");
            RejectUnknownFields(obj, entry.Id);

            string id = RequireString(obj["id"], $"{DefaultRelativePath} entry '{entry.Id}'.id");
            if (!string.Equals(id, entry.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{entry.Id}' id '{id}' does not match its merge key.");
            }

            bool enabled = obj["enabled"]?.GetValue<bool>() ?? true;
            string[] mapIds = ParseStringArray(obj["mapIds"], $"{DefaultRelativePath} entry '{id}'.mapIds", requireNonEmpty: true);
            string[] sourceUris = ParseStringArray(obj["sourceUris"], $"{DefaultRelativePath} entry '{id}'.sourceUris", requireNonEmpty: true);
            if (sourceUris.Length != 1)
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{id}'.sourceUris must contain exactly one URI; backend scenes do not silently fall back between candidates.");
            }
            Vector3 position = ParseVector3(obj["position"], $"{DefaultRelativePath} entry '{id}'.position");
            Quaternion rotation = ParseQuaternion(obj["rotation"], $"{DefaultRelativePath} entry '{id}'.rotation");
            Vector3 scale = ParseVector3(obj["scale"], $"{DefaultRelativePath} entry '{id}'.scale");
            if (scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f)
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{id}'.scale must contain only positive values.");
            }

            result.Add(new RaylibBackendSceneDescriptor(id, enabled, mapIds, sourceUris, position, rotation, scale));
        }

        return result;
    }

    private static void RejectUnknownFields(JsonObject obj, string entryId)
    {
        foreach (KeyValuePair<string, JsonNode?> property in obj)
        {
            bool known = false;
            for (int i = 0; i < Fields.Length; i++)
            {
                if (string.Equals(property.Key, Fields[i], StringComparison.Ordinal))
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                throw new InvalidOperationException(
                    $"{DefaultRelativePath} entry '{entryId}' has unknown field '{property.Key}'.");
            }
        }
    }

    private static string RequireString(JsonNode? node, string context)
    {
        string? value = node?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{context} must be a trimmed non-empty string.");
        }

        return value;
    }

    private static string[] ParseStringArray(JsonNode? node, string context, bool requireNonEmpty)
    {
        if (node is not JsonArray array || (requireNonEmpty && array.Count == 0))
        {
            throw new InvalidOperationException($"{context} must be a non-empty array of strings.");
        }

        var values = new string[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            values[i] = RequireString(array[i], $"{context}[{i}]");
        }

        return values;
    }

    private static Vector3 ParseVector3(JsonNode? node, string context)
    {
        if (node is not JsonArray array || array.Count != 3)
        {
            throw new InvalidOperationException($"{context} must be an array of exactly three finite numbers.");
        }

        Vector3 value = new(ParseFiniteFloat(array[0], $"{context}[0]"), ParseFiniteFloat(array[1], $"{context}[1]"), ParseFiniteFloat(array[2], $"{context}[2]"));
        return value;
    }

    private static Quaternion ParseQuaternion(JsonNode? node, string context)
    {
        if (node is not JsonArray array || array.Count != 4)
        {
            throw new InvalidOperationException($"{context} must be an array [x,y,z,w] of four finite numbers.");
        }

        Quaternion value = new(
            ParseFiniteFloat(array[0], $"{context}[0]"),
            ParseFiniteFloat(array[1], $"{context}[1]"),
            ParseFiniteFloat(array[2], $"{context}[2]"),
            ParseFiniteFloat(array[3], $"{context}[3]"));
        if (value.LengthSquared() <= 0.000001f)
        {
            throw new InvalidOperationException($"{context} must not be a zero quaternion.");
        }

        return Quaternion.Normalize(value);
    }

    private static float ParseFiniteFloat(JsonNode? node, string context)
    {
        float value;
        try
        {
            value = node?.GetValue<float>() ?? throw new InvalidOperationException();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException($"{context} must be a finite number.", ex);
        }

        if (!float.IsFinite(value))
        {
            throw new InvalidOperationException($"{context} must be a finite number.");
        }

        return value;
    }
}

public enum RaylibBackendSceneState : byte
{
    Unrequested = 0,
    Preparing = 1,
    CpuReady = 2,
    UploadQueued = 3,
    Resident = 4,
    Failed = 5,
}

public readonly record struct RaylibBackendSceneResidencySnapshot(
    RaylibBackendSceneState State,
    string? FailureReason,
    int RequiredCount,
    int ResidentCount,
    int InFlightCount,
    int FailedCount)
{
    public bool IsPending => State is RaylibBackendSceneState.Preparing or
        RaylibBackendSceneState.CpuReady or RaylibBackendSceneState.UploadQueued;
}

public interface IRaylibBackendSceneResidency
{
    void BeginMap(string mapId);

    RaylibBackendSceneResidencySnapshot Poll();

    void MarkMapReady(string mapId);

    void Release(string mapId);
}

public sealed class RaylibBackendSceneRuntime : IRaylibBackendSceneResidency, IDisposable
{
    private readonly RaylibPrimitiveRenderer _renderer;
    private readonly List<RaylibBackendSceneDescriptor> _descriptors = new();
    private readonly List<ActiveScene> _active = new();
    private string? _activeMapId;
    private bool _mapReady;
    private bool _disposed;

    public RaylibBackendSceneRuntime(RaylibPrimitiveRenderer renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public string? ActiveMapId => _activeMapId;

    public IReadOnlyList<RaylibBackendSceneDescriptor> Descriptors => _descriptors;

    public void LoadDescriptors(IReadOnlyList<MergedConfigEntry> merged)
    {
        ThrowIfDisposed();
        _descriptors.Clear();
        _descriptors.AddRange(RaylibBackendSceneCatalog.Parse(merged));
    }

    public void BeginMap(string mapId)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new ArgumentException("Backend scene map id must not be empty.", nameof(mapId));
        }

        ReleaseActive();
        _activeMapId = mapId;
        _mapReady = false;
        for (int i = 0; i < _descriptors.Count; i++)
        {
            RaylibBackendSceneDescriptor descriptor = _descriptors[i];
            if (descriptor.Enabled && descriptor.AppliesTo(mapId))
            {
                _active.Add(new ActiveScene(descriptor));
            }
        }
    }

    public RaylibBackendSceneResidencySnapshot Poll()
    {
        ThrowIfDisposed();
        if (_active.Count == 0)
        {
            return new RaylibBackendSceneResidencySnapshot(
                RaylibBackendSceneState.Resident, null, 0, 0, 0, 0);
        }

        int resident = 0;
        int inFlight = 0;
        for (int i = 0; i < _active.Count; i++)
        {
            ActiveScene scene = _active[i];
            if (scene.State == RaylibBackendSceneState.Resident)
            {
                resident++;
                continue;
            }

            if (scene.State == RaylibBackendSceneState.Failed)
            {
                return new RaylibBackendSceneResidencySnapshot(
                    RaylibBackendSceneState.Failed,
                    scene.FailureReason,
                    _active.Count,
                    resident,
                    inFlight,
                    1);
            }

            RaylibAssetAcquireOutcome outcome = _renderer.TryAcquireExternalModel(
                scene.Descriptor.SourceUris[0],
                out RaylibAssetStore<Model>.Lease? lease,
                out string? status);
            if (outcome == RaylibAssetAcquireOutcome.InFlight)
            {
                scene.State = ParseState(status);
                scene.FailureReason = status;
                inFlight++;
                continue;
            }

            if (outcome == RaylibAssetAcquireOutcome.Resident)
            {
                scene.Lease = lease;
                scene.Model = lease!.Resource;
                scene.State = RaylibBackendSceneState.Resident;
                scene.FailureReason = null;
                resident++;
                continue;
            }

            scene.State = RaylibBackendSceneState.Failed;
            scene.FailureReason = $"'{scene.Descriptor.SourceUris[0]}': {status ?? "backend reported Failed"}";
            return new RaylibBackendSceneResidencySnapshot(
                RaylibBackendSceneState.Failed,
                scene.FailureReason,
                _active.Count,
                resident,
                inFlight,
                1);
        }

        if (resident == _active.Count)
        {
            return new RaylibBackendSceneResidencySnapshot(
                RaylibBackendSceneState.Resident, null, _active.Count, resident, 0, 0);
        }

        return new RaylibBackendSceneResidencySnapshot(
            RaylibBackendSceneState.Preparing, null, _active.Count, resident, inFlight, 0);
    }

    public void MarkMapReady(string mapId)
    {
        ThrowIfDisposed();
        if (!string.Equals(_activeMapId, mapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot mark backend scene map '{mapId}' ready while '{_activeMapId ?? "<none>"}' is active.");
        }

        _mapReady = true;
    }

    public int Draw(string mapId)
    {
        ThrowIfDisposed();
        if (!_mapReady || !string.Equals(_activeMapId, mapId, StringComparison.Ordinal))
        {
            return 0;
        }

        int drawn = 0;
        for (int i = 0; i < _active.Count; i++)
        {
            ActiveScene scene = _active[i];
            if (scene.State != RaylibBackendSceneState.Resident || scene.Lease == null)
            {
                throw new InvalidOperationException(
                    $"Backend scene '{scene.Descriptor.Id}' was marked ready without a resident model.");
            }

            _renderer.DrawExternalModel(
                scene.Model,
                scene.Descriptor.Position,
                scene.Descriptor.Rotation,
                scene.Descriptor.Scale,
                Vector4.One);
            drawn++;
        }

        return drawn;
    }

    public void Release(string mapId)
    {
        ThrowIfDisposed();
        if (string.Equals(_activeMapId, mapId, StringComparison.Ordinal))
        {
            ReleaseActive();
            _activeMapId = null;
            _mapReady = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseActive();
        _activeMapId = null;
        _disposed = true;
    }

    private void ReleaseActive()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            _active[i].Lease?.Dispose();
        }

        _active.Clear();
        _mapReady = false;
    }

    private static RaylibBackendSceneState ParseState(string? status)
    {
        return Enum.TryParse(status, ignoreCase: false, out RaylibBackendSceneState state)
            ? state
            : RaylibBackendSceneState.Preparing;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RaylibBackendSceneRuntime));
        }
    }

    private sealed class ActiveScene
    {
        public ActiveScene(RaylibBackendSceneDescriptor descriptor)
        {
            Descriptor = descriptor;
            State = RaylibBackendSceneState.Unrequested;
        }

        public readonly RaylibBackendSceneDescriptor Descriptor;
        public RaylibBackendSceneState State;
        public string? FailureReason;
        public RaylibAssetStore<Model>.Lease? Lease;
        public Model Model;
    }
}

public sealed class RaylibSceneMapLoadCompletionGate : IMapLoadCompletionGate, IMapLoadCompletionGateLifetime, IDisposable
{
    private readonly IMapLoadCompletionGate _coreGate;
    private readonly IMapLoadCompletionGateLifetime? _coreGateLifetime;
    private readonly IRaylibBackendSceneResidency _scenes;
    private readonly Dictionary<MapId, Pending> _active = new();
    private bool _disposed;

    public RaylibSceneMapLoadCompletionGate(
        IMapLoadCompletionGate coreGate,
        IRaylibBackendSceneResidency scenes)
    {
        _coreGate = coreGate ?? throw new ArgumentNullException(nameof(coreGate));
        _coreGateLifetime = coreGate as IMapLoadCompletionGateLifetime;
        _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
    }

    public IPendingMapLoad BeginPendingLoad(in MapLoadCompletionRequest request)
    {
        ThrowIfDisposed();
        Prepare(request.MapId);
        try
        {
            return Register(request.MapId, request.Session, _coreGate.BeginPendingLoad(in request));
        }
        catch
        {
            _scenes.Release(request.MapId.Value);
            throw;
        }
    }

    public IPendingMapLoad BeginPendingResume(in MapResumeCompletionRequest request)
    {
        ThrowIfDisposed();
        MapId mapId = request.ResumedSession.MapId;
        Prepare(mapId);
        try
        {
            return Register(mapId, request.ResumedSession, _coreGate.BeginPendingResume(in request));
        }
        catch
        {
            _scenes.Release(mapId.Value);
            throw;
        }
    }

    public void Release(MapSession session)
    {
        if (session != null && _active.TryGetValue(session.MapId, out Pending? pending) &&
            ReferenceEquals(pending.Session, session))
        {
            pending.Cancel();
        }
        else if (session != null)
        {
            _coreGateLifetime?.Release(session);
            _scenes.Release(session.MapId.Value);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Pending pending in new List<Pending>(_active.Values))
        {
            pending.Cancel();
        }

        _active.Clear();
        (_coreGate as IDisposable)?.Dispose();
        (_scenes as IDisposable)?.Dispose();
    }

    private void Prepare(MapId mapId)
    {
        foreach (Pending previous in new List<Pending>(_active.Values))
        {
            previous.Cancel();
        }

        _scenes.BeginMap(mapId.Value);
    }

    private IPendingMapLoad Register(MapId mapId, MapSession session, IPendingMapLoad corePending)
    {
        var pending = new Pending(_scenes, session, corePending, RemoveIfCurrent);
        _active[mapId] = pending;
        return pending;

        void RemoveIfCurrent(Pending canceled)
        {
            if (_active.TryGetValue(mapId, out Pending? current) && ReferenceEquals(current, canceled))
            {
                _active.Remove(mapId);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RaylibSceneMapLoadCompletionGate));
        }
    }

    private sealed class Pending : IPendingMapLoad
    {
        private readonly IRaylibBackendSceneResidency _scenes;
        private readonly IPendingMapLoad _corePending;
        private readonly Action<Pending> _onCanceled;
        private MapLoadCompletionResult? _terminal;
        private bool _canceled;

        public Pending(
            IRaylibBackendSceneResidency scenes,
            MapSession session,
            IPendingMapLoad corePending,
            Action<Pending> onCanceled)
        {
            _scenes = scenes;
            Session = session ?? throw new ArgumentNullException(nameof(session));
            _corePending = corePending ?? throw new ArgumentNullException(nameof(corePending));
            _onCanceled = onCanceled;
        }

        public MapSession Session { get; }

        public MapLoadCompletionResult Poll()
        {
            if (_canceled)
            {
                return MapLoadCompletionResult.Failed(
                    $"Map '{Session.MapId.Value}' backend scene poll occurred after cancellation.");
            }

            if (_terminal.HasValue)
            {
                return _terminal.Value;
            }

            try
            {
                RaylibBackendSceneResidencySnapshot scene = _scenes.Poll();
                if (scene.State == RaylibBackendSceneState.Failed)
                {
                    return FailAndRelease(
                        $"Raylib backend scene for map '{Session.MapId.Value}' failed: {scene.FailureReason}",
                        scene.RequiredCount,
                        scene.ResidentCount,
                        scene.InFlightCount,
                        scene.FailedCount);
                }

                MapLoadCompletionResult core = _corePending.Poll();
                if (core.State == MapLoadCompletionState.Failed)
                {
                    _scenes.Release(Session.MapId.Value);
                    return SetTerminal(core);
                }

                bool sceneReady = !scene.IsPending && scene.State == RaylibBackendSceneState.Resident;
                bool coreReady = core.State == MapLoadCompletionState.Ready;
                if (sceneReady && coreReady)
                {
                    _scenes.MarkMapReady(Session.MapId.Value);
                    return SetTerminal(MapLoadCompletionResult.Ready(
                        scene.RequiredCount + core.RequiredAssetCount,
                        scene.ResidentCount + core.ResidentAssetCount,
                        0,
                        scene.FailedCount + core.FailedAssetCount));
                }

                return MapLoadCompletionResult.Pending(
                    scene.RequiredCount + core.RequiredAssetCount,
                    scene.ResidentCount + core.ResidentAssetCount,
                    scene.InFlightCount + core.InFlightAssetCount,
                    scene.FailedCount + core.FailedAssetCount);
            }
            catch (Exception ex)
            {
                return FailAndRelease(
                    $"Map '{Session.MapId.Value}' completion rendezvous failed: {ex.Message}");
            }
        }

        public void Cancel()
        {
            if (_canceled)
            {
                return;
            }

            _canceled = true;
            _corePending.Cancel();
            _scenes.Release(Session.MapId.Value);
            _onCanceled(this);
        }

        private MapLoadCompletionResult SetTerminal(MapLoadCompletionResult result)
        {
            _terminal = result;
            return result;
        }

        private MapLoadCompletionResult FailAndRelease(
            string message,
            int requiredAssetCount = 0,
            int residentAssetCount = 0,
            int inFlightAssetCount = 0,
            int failedAssetCount = 0)
        {
            string failure = message;
            try
            {
                _corePending.Cancel();
            }
            catch (Exception ex)
            {
                failure += $"; Core cleanup failed: {ex.Message}";
            }

            try
            {
                _scenes.Release(Session.MapId.Value);
            }
            catch (Exception ex)
            {
                failure += $"; Raylib scene cleanup failed: {ex.Message}";
            }

            return SetTerminal(MapLoadCompletionResult.Failed(
                failure,
                requiredAssetCount,
                residentAssetCount,
                inFlightAssetCount,
                failedAssetCount));
        }
    }
}
