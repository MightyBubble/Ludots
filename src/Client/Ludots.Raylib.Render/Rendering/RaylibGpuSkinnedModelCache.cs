using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    public enum RaylibGpuSkinnedModelAcquireOutcome : byte
    {
        Resident = 0,
        InFlight = 1,
        Failed = 2,
    }

    /// <summary>
    /// Shared model cache for GpuSkinnedInstance. Model upload stays on the render thread;
    /// animation file preparation runs independently and is validated only after both phases
    /// are complete on the render thread.
    /// </summary>
    public sealed unsafe class RaylibGpuSkinnedModelCache : IDisposable
    {
        public const int MaxBones = 128;

        public readonly struct Entry
        {
            public readonly Model Model;
            public readonly ModelAnimation* Animations;
            public readonly int AnimCount;
            public readonly string SourcePath;
            public readonly bool Loaded;

            public Entry(Model model, ModelAnimation* animations, int animCount, string sourcePath, bool loaded)
            {
                Model = model;
                Animations = animations;
                AnimCount = animCount;
                SourcePath = sourcePath;
                Loaded = loaded;
            }
        }

        private sealed class PendingLoad
        {
            public PendingLoad(int meshAssetId, MeshAssetDescriptor descriptor)
            {
                MeshAssetId = meshAssetId;
                Descriptor = descriptor;
            }

            public readonly int MeshAssetId;
            public readonly MeshAssetDescriptor Descriptor;
            public readonly List<string> Failures = new();
            public int CandidateIndex;
            public string? Uri;
            public string? FullPath;
            public RaylibAssetStore<Model>.Lease? ModelLease;
            public Task<AnimationLoadResult>? AnimationTask;
        }

        private readonly struct AnimationLoadResult
        {
            public AnimationLoadResult(ModelAnimation* animations, int animCount, string loadablePath)
            {
                Animations = animations;
                AnimCount = animCount;
                LoadablePath = loadablePath;
            }

            public readonly ModelAnimation* Animations;
            public readonly int AnimCount;
            public readonly string LoadablePath;
        }

        private readonly IRenderAssetPathResolver? _vfs;
        private readonly RaylibAssetStore<Model> _modelStore;
        private readonly Dictionary<int, Entry> _entries = new();
        private readonly Dictionary<int, RaylibAssetStore<Model>.Lease> _leases = new();
        private readonly Dictionary<int, PendingLoad> _pending = new();
        private readonly Dictionary<int, string> _failures = new();
        private readonly Dictionary<int, string> _selectedUris = new();
        private readonly bool _ownsModelStore;
        private readonly bool _synchronous;
        private bool _disposed;

        public RaylibGpuSkinnedModelCache(IRenderAssetPathResolver? vfs, RaylibAssetStore<Model>? modelStore = null)
        {
            _vfs = vfs;
            _ownsModelStore = modelStore == null;
            _synchronous = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_SYNC_ASSET_LOAD") == "1";
            _modelStore = modelStore ?? new RaylibAssetStore<Model>(vfs, fullPath =>
            {
                string loadablePath = RaylibModelFileLoader.PrepareNativeLoadable(fullPath);
                Model model = RaylibNativeResources.LoadModel(loadablePath);
                if (model.meshCount <= 0)
                {
                    RaylibNativeResources.UnloadModel(model);
                    throw new InvalidOperationException($"model '{fullPath}' loaded with meshCount=0.");
                }

                return model;
            }, RaylibNativeResources.UnloadModel);
        }

        /// <summary>
        /// Non-blocking cache request. InFlight means the caller must skip this frame and ask
        /// again after the normal asset pump; it never waits for a worker or GL upload.
        /// </summary>
        public RaylibGpuSkinnedModelAcquireOutcome TryGetOrLoad(
            int meshAssetId,
            in MeshAssetDescriptor descriptor,
            out Entry entry,
            out string? status)
        {
            ThrowIfDisposed();
            entry = default;
            status = null;

            if (_entries.TryGetValue(meshAssetId, out Entry residentOrFailed))
            {
                if (residentOrFailed.Loaded)
                {
                    entry = residentOrFailed;
                    return RaylibGpuSkinnedModelAcquireOutcome.Resident;
                }

                status = _failures.TryGetValue(meshAssetId, out string? failure)
                    ? failure
                    : "previously failed to load";
                return RaylibGpuSkinnedModelAcquireOutcome.Failed;
            }

            if (descriptor.Type != MeshAssetType.Model)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} Type={descriptor.Type}; GpuSkinnedInstance requires MeshAssetType.Model.");
            }

            if (_vfs == null || descriptor.SourceUris == null || descriptor.SourceUris.Length == 0)
            {
                return Fail(meshAssetId,
                    $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} has no VFS/sourceUris for GpuSkinnedInstance.",
                    out status);
            }

            if (!_pending.TryGetValue(meshAssetId, out PendingLoad? pending))
            {
                pending = new PendingLoad(meshAssetId, descriptor);
                _pending.Add(meshAssetId, pending);
            }

            while (true)
            {
                if (pending.ModelLease == null)
                {
                    if (!TryBeginCandidate(pending, out bool candidateInFlight, out string? candidateStatus))
                    {
                        return Fail(meshAssetId, BuildFailure(pending), out status);
                    }

                    if (candidateInFlight)
                    {
                        status = candidateStatus ?? RaylibAssetState.Preparing.ToString();
                        return RaylibGpuSkinnedModelAcquireOutcome.InFlight;
                    }
                }

                RaylibAssetStore<Model>.Lease modelLease = pending.ModelLease
                    ?? throw new InvalidOperationException(
                        $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={meshAssetId} reached validation without a model lease.");
                Model model = modelLease.Resource;
                if (!TryValidateModel(meshAssetId, pending.FullPath!, model, out string? modelFailure))
                {
                    pending.Failures.Add($"'{pending.Uri}': {modelFailure}");
                    ReleasePendingModel(pending);
                    if (TryMoveToNextCandidate(pending))
                    {
                        continue;
                    }

                    return Fail(meshAssetId, BuildFailure(pending), out status);
                }

                if (pending.AnimationTask == null)
                {
                    pending.AnimationTask = BeginAnimationLoad(pending.FullPath!);
                }

                if (!pending.AnimationTask.IsCompleted)
                {
                    status = "animation-preparing";
                    return RaylibGpuSkinnedModelAcquireOutcome.InFlight;
                }

                AnimationLoadResult animation;
                try
                {
                    animation = pending.AnimationTask.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    pending.Failures.Add($"'{pending.Uri}': {ex.GetBaseException().Message}");
                    ReleasePendingModel(pending);
                    if (TryMoveToNextCandidate(pending))
                    {
                        continue;
                    }

                    return Fail(meshAssetId, BuildFailure(pending), out status);
                }

                try
                {
                    ValidateAnimations(meshAssetId, pending.FullPath!, model, animation);
                }
                catch (Exception ex)
                {
                    UnloadAnimations(animation);
                    pending.AnimationTask = null;
                    pending.Failures.Add($"'{pending.Uri}': {ex.Message}");
                    ReleasePendingModel(pending);
                    if (TryMoveToNextCandidate(pending))
                    {
                        continue;
                    }

                    return Fail(meshAssetId, BuildFailure(pending), out status);
                }

                Entry completed = new(model, animation.Animations, animation.AnimCount, pending.FullPath!, loaded: true);
                _entries[meshAssetId] = completed;
                _leases[meshAssetId] = modelLease;
                _selectedUris[meshAssetId] = pending.Uri!;
                _pending.Remove(meshAssetId);
                entry = completed;
                return RaylibGpuSkinnedModelAcquireOutcome.Resident;
            }
        }

        public Entry GetOrLoad(int meshAssetId, in MeshAssetDescriptor descriptor)
        {
            ThrowIfDisposed();
            while (true)
            {
                RaylibGpuSkinnedModelAcquireOutcome outcome = TryGetOrLoad(meshAssetId, in descriptor, out Entry entry, out string? status);
                if (outcome == RaylibGpuSkinnedModelAcquireOutcome.Resident)
                {
                    return entry;
                }

                if (outcome == RaylibGpuSkinnedModelAcquireOutcome.Failed)
                {
                    throw new InvalidOperationException(status);
                }

                if (_pending.TryGetValue(meshAssetId, out PendingLoad? pending))
                {
                    if (pending.ModelLease == null && pending.Uri != null)
                    {
                        if (!_modelStore.TryAcquire(pending.Uri, out RaylibAssetStore<Model>.Lease? lease, out string? failure))
                        {
                            pending.Failures.Add($"'{pending.Uri}': {failure}");
                            ReleasePendingModel(pending);
                            TryMoveToNextCandidate(pending);
                            continue;
                        }

                        pending.ModelLease = lease;
                    }

                    if (pending.AnimationTask != null)
                    {
                        try
                        {
                            pending.AnimationTask.Wait();
                        }
                        catch (AggregateException)
                        {
                            // The next TryGetOrLoad iteration converts the task failure into the
                            // cache's fail-loud candidate-chain result and releases its model lease.
                        }
                    }
                }

                Thread.Yield();
            }
        }

        public int AnimationInFlightCount
        {
            get
            {
                int count = 0;
                foreach (PendingLoad pending in _pending.Values)
                {
                    if (pending.ModelLease != null && pending.AnimationTask is { IsCompleted: false })
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public bool TryGetSelectedSourceUri(int meshAssetId, out string sourceUri)
            => _selectedUris.TryGetValue(meshAssetId, out sourceUri!);

        public void UnloadAll(Action<Model>? beforeUnloadModel = null)
        {
            if (_disposed)
            {
                return;
            }

            foreach (PendingLoad pending in _pending.Values)
            {
                DisposeAnimationTask(pending.AnimationTask);
                ReleasePendingModel(pending);
            }

            _pending.Clear();
            foreach (KeyValuePair<int, Entry> kvp in _entries)
            {
                Entry entry = kvp.Value;
                if (!entry.Loaded)
                {
                    continue;
                }

                beforeUnloadModel?.Invoke(entry.Model);
                UnloadAnimations(new AnimationLoadResult(entry.Animations, entry.AnimCount, entry.SourcePath));
                if (_leases.TryGetValue(kvp.Key, out RaylibAssetStore<Model>.Lease? lease))
                {
                    lease.Dispose();
                }
            }

            _entries.Clear();
            _leases.Clear();
            _failures.Clear();
            _selectedUris.Clear();
        }

        public void Dispose()
        {
            UnloadAll();
            if (_ownsModelStore)
            {
                _modelStore.Dispose();
            }

            _disposed = true;
        }

        private bool TryBeginCandidate(PendingLoad pending, out bool inFlight, out string? status)
        {
            inFlight = false;
            status = null;
            string[] uris = pending.Descriptor.SourceUris ?? Array.Empty<string>();

            // A candidate that returned InFlight remains the active candidate. Poll the
            // same URI until its shared store lease becomes Resident; advancing the chain
            // here would orphan the in-flight entry and could load a fallback in parallel.
            if (pending.Uri != null)
            {
                RaylibAssetAcquireOutcome activeOutcome = _modelStore.TryAcquireOrBegin(
                    pending.Uri,
                    out RaylibAssetStore<Model>.Lease? activeLease,
                    out string? activeFailure);
                if (activeOutcome == RaylibAssetAcquireOutcome.Resident)
                {
                    pending.ModelLease = activeLease;
                    return true;
                }

                if (activeOutcome == RaylibAssetAcquireOutcome.InFlight)
                {
                    inFlight = true;
                    status = activeFailure;
                    return true;
                }

                pending.Failures.Add($"'{pending.Uri}': {activeFailure}");
                pending.Uri = null;
                pending.FullPath = null;
            }

            while (pending.CandidateIndex < uris.Length)
            {
                string uri = uris[pending.CandidateIndex++];
                if (string.IsNullOrWhiteSpace(uri))
                {
                    continue;
                }

                if (!_vfs!.TryResolveFullPath(uri, out string fullPath) || !File.Exists(fullPath))
                {
                    pending.Failures.Add($"'{uri}': file missing or URI does not resolve");
                    continue;
                }

                pending.Uri = uri;
                pending.FullPath = fullPath;
                RaylibAssetAcquireOutcome outcome = _modelStore.TryAcquireOrBegin(uri, out RaylibAssetStore<Model>.Lease? lease, out string? failure);
                if (outcome == RaylibAssetAcquireOutcome.Failed)
                {
                    pending.Failures.Add($"'{uri}': {failure}");
                    pending.Uri = null;
                    pending.FullPath = null;
                    continue;
                }

                if (outcome == RaylibAssetAcquireOutcome.InFlight)
                {
                    inFlight = true;
                    status = failure;
                    return true;
                }

                pending.ModelLease = lease;
                return true;
            }

            return false;
        }

        private Task<AnimationLoadResult> BeginAnimationLoad(string fullPath)
        {
            if (!_synchronous)
            {
                return Task.Run(() => LoadAnimations(fullPath));
            }

            try
            {
                return Task.FromResult(LoadAnimations(fullPath));
            }
            catch (Exception ex)
            {
                return Task.FromException<AnimationLoadResult>(ex);
            }
        }

        private static AnimationLoadResult LoadAnimations(string fullPath)
        {
            string loadablePath = RaylibModelFileLoader.PrepareNativeLoadable(fullPath);
            ModelAnimation* animations = Rl.LoadModelAnimations(loadablePath, out int animCount);
            if (animations == null || animCount <= 0)
            {
                if (animations != null)
                {
                    Rl.UnloadModelAnimations(animations, animCount);
                }

                throw new InvalidOperationException($"path='{fullPath}' loaded with animCount={animCount}; GpuSkinnedInstance requires animations.");
            }

            return new AnimationLoadResult(animations, animCount, loadablePath);
        }

        private static bool TryValidateModel(int meshAssetId, string fullPath, Model model, out string? failure)
        {
            if (model.boneCount <= 0)
            {
                failure = $"meshAssetId={meshAssetId} path='{fullPath}' has boneCount=0; GpuSkinnedInstance requires a skinned model.";
                return false;
            }

            if (model.boneCount > MaxBones)
            {
                failure = $"meshAssetId={meshAssetId} path='{fullPath}' boneCount={model.boneCount} exceeds MAX_BONE_NUM={MaxBones}.";
                return false;
            }

            failure = null;
            return true;
        }

        private static void ValidateAnimations(int meshAssetId, string fullPath, Model model, AnimationLoadResult animation)
        {
            for (int i = 0; i < animation.AnimCount; i++)
            {
                ModelAnimation anim = animation.Animations[i];
                if (!Rl.IsModelAnimationValid(model, anim))
                {
                    throw new InvalidOperationException(
                        $"meshAssetId={meshAssetId} path='{fullPath}' animation[{i}] failed IsModelAnimationValid (modelBones={model.boneCount}, animBones={anim.boneCount}).");
                }
            }
        }

        private bool TryMoveToNextCandidate(PendingLoad pending)
        {
            DisposeAnimationTask(pending.AnimationTask);
            pending.AnimationTask = null;
            pending.Uri = null;
            pending.FullPath = null;
            return pending.CandidateIndex < (pending.Descriptor.SourceUris?.Length ?? 0);
        }

        private void ReleasePendingModel(PendingLoad pending)
        {
            pending.ModelLease?.Dispose();
            pending.ModelLease = null;
        }

        private static void DisposeAnimationTask(Task<AnimationLoadResult>? task)
        {
            if (task == null)
            {
                return;
            }

            if (task.IsCompletedSuccessfully)
            {
                UnloadAnimations(task.Result);
                return;
            }

            if (!task.IsCompleted)
            {
                _ = task.ContinueWith(
                    completed =>
                    {
                        if (completed.Status == TaskStatus.RanToCompletion)
                        {
                            UnloadAnimations(completed.Result);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        private static void UnloadAnimations(AnimationLoadResult animation)
        {
            if (animation.Animations != null && animation.AnimCount > 0)
            {
                Rl.UnloadModelAnimations(animation.Animations, animation.AnimCount);
            }
        }

        private RaylibGpuSkinnedModelAcquireOutcome Fail(int meshAssetId, string reason, out string? status)
        {
            _pending.Remove(meshAssetId);
            _failures[meshAssetId] = reason;
            _selectedUris.Remove(meshAssetId);
            _entries[meshAssetId] = default;
            status = reason;
            return RaylibGpuSkinnedModelAcquireOutcome.Failed;
        }

        private static string BuildFailure(PendingLoad pending) =>
            $"{nameof(RaylibGpuSkinnedModelCache)} meshAssetId={pending.MeshAssetId} could not load a valid skinned model. Attempts: [{string.Join("; ", pending.Failures)}]";

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibGpuSkinnedModelCache));
            }
        }
    }
}
