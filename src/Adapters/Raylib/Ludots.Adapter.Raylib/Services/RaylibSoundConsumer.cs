using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Requests;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib.Services
{
    public sealed record RaylibSoundAttenuationConfig
    {
        public float ReferenceDistanceMeters { get; init; } = 5f;

        public float MaxDistanceMeters { get; init; } = 45f;

        public RaylibSoundAttenuationConfig Validate()
        {
            if (!float.IsFinite(ReferenceDistanceMeters) || ReferenceDistanceMeters < 0f)
            {
                throw new InvalidOperationException(
                    $"Sound attenuation referenceDistanceMeters must be finite and non-negative, got {ReferenceDistanceMeters}.");
            }

            if (!float.IsFinite(MaxDistanceMeters) || MaxDistanceMeters <= ReferenceDistanceMeters)
            {
                throw new InvalidOperationException(
                    $"Sound attenuation maxDistanceMeters must be finite and greater than referenceDistanceMeters ({ReferenceDistanceMeters}), got {MaxDistanceMeters}.");
            }

            return this;
        }
    }

    internal interface IRaylibSoundBackend
    {
        void InitDevice();

        void CloseDevice();

        bool IsDeviceReady();

        Sound LoadFromFile(string path);

        Sound CreateAlias(Sound source);

        void UnloadAlias(in Sound alias);

        void UnloadBase(in Sound sound);

        bool IsPlaying(in Sound alias);

        void Play(in Sound alias);

        void Stop(in Sound alias);

        void SetVolume(in Sound alias, float volume);
    }

    internal sealed class RaylibNativeSoundBackend : IRaylibSoundBackend
    {
        public void InitDevice() => Rl.InitAudioDevice();

        public void CloseDevice() => Rl.CloseAudioDevice();

        public bool IsDeviceReady() => Rl.IsAudioDeviceReady();

        public Sound LoadFromFile(string path) => RaylibNativeResources.LoadSound(path);

        public Sound CreateAlias(Sound source) => RaylibNativeResources.LoadSoundAlias(source);

        public void UnloadAlias(in Sound alias) => RaylibNativeResources.UnloadSoundAlias(alias);

        public void UnloadBase(in Sound sound) => RaylibNativeResources.UnloadSound(sound);

        public bool IsPlaying(in Sound alias) => Rl.IsSoundPlaying(alias);

        public void Play(in Sound alias) => Rl.PlaySound(alias);

        public void Stop(in Sound alias) => Rl.StopSound(alias);

        public void SetVolume(in Sound alias, float volume) => Rl.SetSoundVolume(alias, volume);
    }

    /// <summary>
    /// SoundRequestBuffer 的 raylib 消费端：stableId 生命周期对齐 buffer 合同
    /// （PlayOrUpdate 每帧可重复、Stop 停止并释放、资产经 host_assets Sound 行绑定 SourceUris）。
    /// 循环音在原生播放结束后按请求补触发（raylib Sound 无原生 loop 位）；一次性音播完即静默等待 Stop。
    /// </summary>
    public sealed class RaylibSoundConsumer : IDisposable
    {
        private readonly IRaylibSoundBackend _backend;
        private readonly MeshAssetRegistry _assets;
        private readonly IVirtualFileSystem _vfs;
        private readonly RaylibSoundAttenuationConfig _attenuation;
        private readonly Dictionary<int, ActiveSound> _activeByStableId = new();
        private readonly Dictionary<int, Sound> _baseByAssetId = new();
        private readonly HashSet<int> _failedAssetIds = new();
        private bool _deviceReady;
        private bool _deviceFailureReported;
        private bool _disposed;

        private struct ActiveSound
        {
            public Sound Alias;

            public int SoundAssetId;

            public bool Loop;

            public float LastAppliedVolume;
        }

        public RaylibSoundConsumer(
            MeshAssetRegistry assets,
            IVirtualFileSystem vfs,
            RaylibSoundAttenuationConfig? attenuation = null)
            : this(assets, vfs, attenuation, backend: null)
        {
        }

        internal RaylibSoundConsumer(
            MeshAssetRegistry assets,
            IVirtualFileSystem vfs,
            RaylibSoundAttenuationConfig? attenuation,
            IRaylibSoundBackend? backend)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
            _attenuation = (attenuation ?? new RaylibSoundAttenuationConfig()).Validate();
            _backend = backend ?? new RaylibNativeSoundBackend();
        }

        public int ActiveCount => _activeByStableId.Count;

        public bool DeviceReady => _deviceReady;

        public bool TryGetActiveVolume(int stableId, out float volume)
        {
            if (_activeByStableId.TryGetValue(stableId, out ActiveSound entry))
            {
                volume = entry.LastAppliedVolume;
                return true;
            }

            volume = 0f;
            return false;
        }

        public void InitializeDevice()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_deviceReady)
            {
                return;
            }

            _backend.InitDevice();
            _deviceReady = _backend.IsDeviceReady();
            if (!_deviceReady && !_deviceFailureReported)
            {
                _deviceFailureReported = true;
                RenderDiagnostics.Warn(
                    "RaylibSoundConsumer: audio device initialization failed; sound requests will be skipped this session.");
            }
        }

        public void Consume(ReadOnlySpan<SoundRequest> requests, Vector3 listenerPositionMeters)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_deviceReady || requests.IsEmpty)
            {
                return;
            }

            for (int i = 0; i < requests.Length; i++)
            {
                ref readonly SoundRequest request = ref requests[i];
                switch (request.Kind)
                {
                    case SoundRequestKind.PlayOrUpdate:
                        ApplyPlayOrUpdate(in request, listenerPositionMeters);
                        break;
                    case SoundRequestKind.Stop:
                        ApplyStop(request.StableId);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"RaylibSoundConsumer received unknown SoundRequestKind {request.Kind} for stableId={request.StableId}.");
                }
            }
        }

        private void ApplyPlayOrUpdate(in SoundRequest request, Vector3 listenerPositionMeters)
        {
            if (!_activeByStableId.TryGetValue(request.StableId, out ActiveSound entry))
            {
                if (!TryCreateAlias(request.StableId, request.SoundAssetId, out Sound alias))
                {
                    return;
                }

                entry = new ActiveSound
                {
                    Alias = alias,
                    SoundAssetId = request.SoundAssetId,
                    Loop = request.Loop,
                    LastAppliedVolume = -1f,
                };
                _activeByStableId.Add(request.StableId, entry);
                ApplyVolume(ref entry, in request, listenerPositionMeters);
                _activeByStableId[request.StableId] = entry;
                _backend.Play(entry.Alias);
                return;
            }

            if (entry.SoundAssetId != request.SoundAssetId)
            {
                _backend.Stop(entry.Alias);
                _backend.UnloadAlias(entry.Alias);
                _activeByStableId.Remove(request.StableId);
                ApplyPlayOrUpdate(in request, listenerPositionMeters);
                return;
            }

            entry.Loop = request.Loop;
            ApplyVolume(ref entry, in request, listenerPositionMeters);
            if (entry.Loop && !_backend.IsPlaying(entry.Alias))
            {
                _backend.Play(entry.Alias);
            }

            _activeByStableId[request.StableId] = entry;
        }

        private void ApplyVolume(ref ActiveSound entry, in SoundRequest request, Vector3 listenerPositionMeters)
        {
            float attenuation = AttenuationFactor(Vector3.Distance(listenerPositionMeters, request.WorldPosition));
            float volume = Math.Clamp(request.Volume, 0f, 1f) * attenuation;
            if (Math.Abs(volume - entry.LastAppliedVolume) > 0.0001f)
            {
                _backend.SetVolume(entry.Alias, volume);
                entry.LastAppliedVolume = volume;
            }
        }

        internal float AttenuationFactor(float distanceMeters)
        {
            if (distanceMeters <= _attenuation.ReferenceDistanceMeters)
            {
                return 1f;
            }

            if (distanceMeters >= _attenuation.MaxDistanceMeters)
            {
                return 0f;
            }

            float span = _attenuation.MaxDistanceMeters - _attenuation.ReferenceDistanceMeters;
            return 1f - ((distanceMeters - _attenuation.ReferenceDistanceMeters) / span);
        }

        private bool TryCreateAlias(int stableId, int soundAssetId, out Sound alias)
        {
            alias = default;
            if (_failedAssetIds.Contains(soundAssetId))
            {
                return false;
            }

            if (!_baseByAssetId.TryGetValue(soundAssetId, out Sound baseSound))
            {
                if (!TryResolveAssetPath(soundAssetId, out string? path))
                {
                    _failedAssetIds.Add(soundAssetId);
                    return false;
                }

                baseSound = _backend.LoadFromFile(path);
                _baseByAssetId[soundAssetId] = baseSound;
            }

            alias = _backend.CreateAlias(baseSound);
            return true;
        }

        private bool TryResolveAssetPath(int soundAssetId, out string? path)
        {
            path = null;
            if (!_assets.TryGetDescriptor(soundAssetId, out MeshAssetDescriptor descriptor) ||
                descriptor.SourceUris is not { Length: > 0 })
            {
                RenderDiagnostics.Warn(
                    $"RaylibSoundConsumer: soundAssetId={soundAssetId} '{_assets.GetName(soundAssetId)}' has no host_assets Sound binding; sound requests for it are skipped.");
                return false;
            }

            for (int i = 0; i < descriptor.SourceUris.Length; i++)
            {
                string uri = descriptor.SourceUris[i];
                if (string.IsNullOrWhiteSpace(uri))
                {
                    continue;
                }

                if (_vfs.TryResolveFullPath(uri, out string fullPath) && File.Exists(fullPath))
                {
                    path = fullPath;
                    return true;
                }
            }

            RenderDiagnostics.Warn(
                $"RaylibSoundConsumer: soundAssetId={soundAssetId} '{_assets.GetName(soundAssetId)}' sourceUris resolved to no existing file; sound requests for it are skipped.");
            return false;
        }

        private void ApplyStop(int stableId)
        {
            if (!_activeByStableId.TryGetValue(stableId, out ActiveSound entry))
            {
                return;
            }

            _backend.Stop(entry.Alias);
            _backend.UnloadAlias(entry.Alias);
            _activeByStableId.Remove(stableId);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            foreach (KeyValuePair<int, ActiveSound> pair in _activeByStableId)
            {
                _backend.Stop(pair.Value.Alias);
                _backend.UnloadAlias(pair.Value.Alias);
            }

            _activeByStableId.Clear();

            foreach (KeyValuePair<int, Sound> pair in _baseByAssetId)
            {
                _backend.UnloadBase(pair.Value);
            }

            _baseByAssetId.Clear();

            if (_deviceReady)
            {
                _backend.CloseDevice();
            }

            _deviceReady = false;
            _disposed = true;
        }
    }
}
