using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Adapter.Raylib.Services;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Requests;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using Raylib_cs;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibSoundConsumerStateTests
{
    private static readonly Vector3 ListenerAtOrigin = Vector3.Zero;

    private FakeSoundBackend _backend = null!;
    private MeshAssetRegistry _assets = null!;
    private VirtualFileSystem _vfs = null!;
    private string _root = null!;
    private int _toneAssetId;

    [SetUp]
    public void SetUp()
    {
        _backend = new FakeSoundBackend();
        _assets = new MeshAssetRegistry();
        _vfs = new VirtualFileSystem();
        _root = Path.Combine(Path.GetTempPath(), "ludots-sound-consumer-tests-" + Guid.NewGuid().ToString("N"));
        string soundsDir = Path.Combine(_root, "assets", "Sounds");
        Directory.CreateDirectory(soundsDir);
        File.WriteAllText(Path.Combine(soundsDir, "tone.wav"), "RIFF-fake");
        _vfs.Mount("SoundTestMod", _root);

        _toneAssetId = _assets.Register("sound_test.tone", MeshAssetDescriptor.Primitive(0, PrimitiveMeshKind.Cube));
        _assets.Register("sound_test.tone", new MeshAssetDescriptor
        {
            Type = MeshAssetType.Primitive,
            PrimitiveKind = PrimitiveMeshKind.Cube,
            SourceUris = new[] { "SoundTestMod:assets/Sounds/tone.wav" },
        });
    }

    [TearDown]
    public void TearDown()
    {
        _consumer?.Dispose();
        _consumer = null;
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private RaylibSoundConsumer? _consumer;

    private RaylibSoundConsumer CreateConsumer(RaylibSoundAttenuationConfig? config = null)
    {
        _consumer = new RaylibSoundConsumer(_assets, _vfs, config, _backend);
        _consumer.InitializeDevice();
        return _consumer;
    }

    private static SoundRequest PlayOrUpdate(int stableId, int assetId, bool loop, float volume, Vector3 position) => new()
    {
        Kind = SoundRequestKind.PlayOrUpdate,
        StableId = stableId,
        SoundAssetId = assetId,
        Loop = loop,
        Volume = volume,
        WorldPosition = position,
    };

    [Test]
    public void InitializeDevice_WhenBackendDeviceNotReady_StaysDisabledAndWarnsExplicitly()
    {
        _backend.DeviceReady = false;
        var warnings = new List<string>();
        Ludots.Raylib.Render.RenderDiagnostics.WarnSink = warnings.Add;
        try
        {
            RaylibSoundConsumer consumer = CreateConsumer();

            Assert.That(consumer.DeviceReady, Is.False);
            consumer.Consume(new[] { PlayOrUpdate(1, _toneAssetId, true, 1f, ListenerAtOrigin) }, ListenerAtOrigin);
            Assert.That(consumer.ActiveCount, Is.Zero);
            Assert.That(_backend.Count("load"), Is.Zero, "no asset work may happen without a device");
            Assert.That(_backend.Count("alias"), Is.Zero);
            Assert.That(_backend.Count("play"), Is.Zero);
            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("audio device initialization failed"));
        }
        finally
        {
            Ludots.Raylib.Render.RenderDiagnostics.WarnSink = null;
        }
    }

    [Test]
    public void PlayOrUpdate_NewStableId_LoadsAliasPlaysOnceAndAppliesVolume()
    {
        RaylibSoundConsumer consumer = CreateConsumer();
        var request = PlayOrUpdate(7, _toneAssetId, loop: true, volume: 1f, ListenerAtOrigin);

        consumer.Consume(new[] { request }, ListenerAtOrigin);
        consumer.Consume(new[] { request }, ListenerAtOrigin);
        consumer.Consume(new[] { request }, ListenerAtOrigin);

        Assert.That(consumer.ActiveCount, Is.EqualTo(1));
        Assert.That(consumer.TryGetActiveVolume(7, out float volume), Is.True);
        Assert.That(volume, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(_backend.Count("load"), Is.EqualTo(1), "base sound must load once per asset");
        Assert.That(_backend.Count("alias"), Is.EqualTo(1), "one alias per stableId");
        Assert.That(_backend.Count("play"), Is.EqualTo(1), "repeated PlayOrUpdate must not restart playback");
        Assert.That(_backend.Count("volume"), Is.GreaterThanOrEqualTo(1));
        Assert.That(_backend.LastVolume, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void PlayOrUpdate_MovingSource_AppliesLinearDistanceAttenuation()
    {
        RaylibSoundConsumer consumer = CreateConsumer(new RaylibSoundAttenuationConfig
        {
            ReferenceDistanceMeters = 5f,
            MaxDistanceMeters = 45f,
        });

        consumer.Consume(new[] { PlayOrUpdate(9, _toneAssetId, true, 1f, new Vector3(5f, 0, 0)) }, ListenerAtOrigin);
        Assert.That(consumer.TryGetActiveVolume(9, out float nearVolume), Is.True);
        Assert.That(nearVolume, Is.EqualTo(1f).Within(0.0001f), "inside reference distance stays full");

        consumer.Consume(new[] { PlayOrUpdate(9, _toneAssetId, true, 1f, new Vector3(25f, 0, 0)) }, ListenerAtOrigin);
        Assert.That(consumer.TryGetActiveVolume(9, out float midVolume), Is.True);
        Assert.That(midVolume, Is.EqualTo(0.5f).Within(0.001f), "midway linear rolloff");

        consumer.Consume(new[] { PlayOrUpdate(9, _toneAssetId, true, 1f, new Vector3(50f, 0, 0)) }, ListenerAtOrigin);
        Assert.That(consumer.TryGetActiveVolume(9, out float farVolume), Is.True);
        Assert.That(farVolume, Is.EqualTo(0f).Within(0.0001f), "beyond max distance is silent");
    }

    [Test]
    public void PlayOrUpdate_LoopSoundThatFinishedPlaying_RetrigersPlayback()
    {
        RaylibSoundConsumer consumer = CreateConsumer();
        var request = PlayOrUpdate(11, _toneAssetId, loop: true, volume: 1f, ListenerAtOrigin);
        consumer.Consume(new[] { request }, ListenerAtOrigin);
        Assert.That(_backend.Count("play"), Is.EqualTo(1));

        _backend.SetAllPlaying(false);
        consumer.Consume(new[] { request }, ListenerAtOrigin);
        Assert.That(_backend.Count("play"), Is.EqualTo(2), "loop sounds must retrigger after native completion");
    }

    [Test]
    public void PlayOrUpdate_OneShotThatFinishedPlaying_DoesNotRetrigger()
    {
        RaylibSoundConsumer consumer = CreateConsumer();
        var request = PlayOrUpdate(13, _toneAssetId, loop: false, volume: 1f, ListenerAtOrigin);
        consumer.Consume(new[] { request }, ListenerAtOrigin);

        _backend.SetAllPlaying(false);
        consumer.Consume(new[] { request }, ListenerAtOrigin);
        consumer.Consume(new[] { request }, ListenerAtOrigin);

        Assert.That(_backend.Count("play"), Is.EqualTo(1), "one-shot must not retrigger on repeated PlayOrUpdate");
        Assert.That(consumer.ActiveCount, Is.EqualTo(1), "entry stays until the Stop request arrives");
    }

    [Test]
    public void PlayOrUpdate_AssetWithoutHostBinding_WarnsOncePerAssetAndCreatesNoEntry()
    {
        int unboundAssetId = _assets.Register("sound_test.unbound", MeshAssetDescriptor.Primitive(0, PrimitiveMeshKind.Cube));
        var warnings = new List<string>();
        Ludots.Raylib.Render.RenderDiagnostics.WarnSink = warnings.Add;
        try
        {
            RaylibSoundConsumer consumer = CreateConsumer();
            var request = PlayOrUpdate(17, unboundAssetId, loop: true, volume: 1f, ListenerAtOrigin);

            consumer.Consume(new[] { request }, ListenerAtOrigin);
            consumer.Consume(new[] { request }, ListenerAtOrigin);
            consumer.Consume(new[] { request }, ListenerAtOrigin);

            Assert.That(consumer.ActiveCount, Is.Zero);
            Assert.That(_backend.Count("load"), Is.Zero);
            Assert.That(warnings.Count, Is.EqualTo(1), "missing binding warns once per asset");
            Assert.That(warnings[0], Does.Contain("sound_test.unbound"));
        }
        finally
        {
            Ludots.Raylib.Render.RenderDiagnostics.WarnSink = null;
        }
    }

    [Test]
    public void PlayOrUpdate_AssetSwapOnSameStableId_ReplacesAlias()
    {
        int secondAssetId = _assets.Register("sound_test.tone2", new MeshAssetDescriptor
        {
            Type = MeshAssetType.Primitive,
            PrimitiveKind = PrimitiveMeshKind.Cube,
            SourceUris = new[] { "SoundTestMod:assets/Sounds/tone.wav" },
        });
        RaylibSoundConsumer consumer = CreateConsumer();

        consumer.Consume(new[] { PlayOrUpdate(19, _toneAssetId, true, 1f, ListenerAtOrigin) }, ListenerAtOrigin);
        consumer.Consume(new[] { PlayOrUpdate(19, secondAssetId, true, 1f, ListenerAtOrigin) }, ListenerAtOrigin);

        Assert.That(consumer.ActiveCount, Is.EqualTo(1));
        Assert.That(_backend.Count("alias"), Is.EqualTo(2), "swap allocates a fresh alias for the new asset");
        Assert.That(_backend.Count("unload_alias"), Is.EqualTo(1), "swap releases the previous alias");
        Assert.That(_backend.Count("stop"), Is.EqualTo(1));
    }

    [Test]
    public void Stop_ReleasesAliasAndEntryAndIsNoOpForUnknownStableId()
    {
        RaylibSoundConsumer consumer = CreateConsumer();
        consumer.Consume(new[] { PlayOrUpdate(21, _toneAssetId, true, 1f, ListenerAtOrigin) }, ListenerAtOrigin);
        Assert.That(consumer.ActiveCount, Is.EqualTo(1));

        var stop = new SoundRequest { Kind = SoundRequestKind.Stop, StableId = 21, SoundAssetId = _toneAssetId };
        consumer.Consume(new[] { stop }, ListenerAtOrigin);
        consumer.Consume(new[] { stop }, ListenerAtOrigin);

        Assert.That(consumer.ActiveCount, Is.Zero);
        Assert.That(consumer.TryGetActiveVolume(21, out _), Is.False);
        Assert.That(_backend.Count("stop"), Is.EqualTo(1), "duplicate Stop must not call the backend again");
        Assert.That(_backend.Count("unload_alias"), Is.EqualTo(1));
    }

    [Test]
    public void TwoStableIdsOnTheSameAsset_ShareBaseSoundWithIndependentAliases()
    {
        RaylibSoundConsumer consumer = CreateConsumer();
        consumer.Consume(new[]
        {
            PlayOrUpdate(31, _toneAssetId, true, 1f, ListenerAtOrigin),
            PlayOrUpdate(32, _toneAssetId, true, 1f, ListenerAtOrigin),
        }, ListenerAtOrigin);

        Assert.That(_backend.Count("load"), Is.EqualTo(1));
        Assert.That(_backend.Count("alias"), Is.EqualTo(2));
        Assert.That(consumer.ActiveCount, Is.EqualTo(2));

        consumer.Consume(new[] { new SoundRequest { Kind = SoundRequestKind.Stop, StableId = 31 } }, ListenerAtOrigin);
        Assert.That(consumer.ActiveCount, Is.EqualTo(1));
        Assert.That(_backend.Count("unload_base"), Is.Zero, "base sound must outlive its aliases");
    }

    [Test]
    public void Dispose_StopsAndUnloadsEverythingAndClosesOwnedDevice()
    {
        RaylibSoundConsumer consumer = CreateConsumer();
        consumer.Consume(new[]
        {
            PlayOrUpdate(41, _toneAssetId, true, 1f, ListenerAtOrigin),
            PlayOrUpdate(42, _toneAssetId, true, 1f, ListenerAtOrigin),
        }, ListenerAtOrigin);

        consumer.Dispose();

        Assert.That(_backend.Count("stop"), Is.EqualTo(2));
        Assert.That(_backend.Count("unload_alias"), Is.EqualTo(2));
        Assert.That(_backend.Count("unload_base"), Is.EqualTo(1));
        Assert.That(_backend.Count("close"), Is.EqualTo(1), "consumer that owns the device lifecycle closes it");
    }

    [Test]
    public void Consume_AfterDispose_Throws()
    {
        RaylibSoundConsumer consumer = CreateConsumer();
        consumer.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            consumer.Consume(new[] { PlayOrUpdate(1, _toneAssetId, true, 1f, ListenerAtOrigin) }, ListenerAtOrigin));
    }

    [Test]
    public void AttenuationConfig_InvalidDistances_FailLoud()
    {
        Assert.Throws<InvalidOperationException>(() => new RaylibSoundAttenuationConfig
        {
            ReferenceDistanceMeters = -1f,
        }.Validate());
        Assert.Throws<InvalidOperationException>(() => new RaylibSoundAttenuationConfig
        {
            ReferenceDistanceMeters = 10f,
            MaxDistanceMeters = 10f,
        }.Validate());
    }

    private sealed class FakeSoundBackend : IRaylibSoundBackend
    {
        private readonly List<string> _calls = new();
        private bool _anyPlaying;
        private int _nextSoundId;

        public bool DeviceReady { get; set; } = true;

        public float LastVolume { get; private set; } = float.NaN;

        public IReadOnlyList<string> Calls => _calls;

        public int Count(string op) => _calls.FindAll(c => c.StartsWith(op + ":", StringComparison.Ordinal)).Count;

        public void SetAllPlaying(bool playing) => _anyPlaying = playing;

        void IRaylibSoundBackend.InitDevice() => _calls.Add("init:");

        void IRaylibSoundBackend.CloseDevice() => _calls.Add("close:");

        bool IRaylibSoundBackend.IsDeviceReady() => DeviceReady;

        Sound IRaylibSoundBackend.LoadFromFile(string path)
        {
            _calls.Add($"load:{path}");
            return NewSound();
        }

        Sound IRaylibSoundBackend.CreateAlias(Sound source)
        {
            _calls.Add($"alias:{_nextSoundId}");
            return NewSound();
        }

        void IRaylibSoundBackend.UnloadAlias(in Sound alias) => _calls.Add($"unload_alias:{alias.frameCount}");

        void IRaylibSoundBackend.UnloadBase(in Sound sound) => _calls.Add($"unload_base:{sound.frameCount}");

        bool IRaylibSoundBackend.IsPlaying(in Sound alias) => _anyPlaying;

        void IRaylibSoundBackend.Play(in Sound alias)
        {
            _calls.Add($"play:{alias.frameCount}");
            _anyPlaying = true;
        }

        void IRaylibSoundBackend.Stop(in Sound alias)
        {
            _calls.Add($"stop:{alias.frameCount}");
            _anyPlaying = false;
        }

        void IRaylibSoundBackend.SetVolume(in Sound alias, float volume)
        {
            _calls.Add($"volume:{alias.frameCount}");
            LastVolume = volume;
        }

        private Sound NewSound()
        {
            _nextSoundId++;
            return new Sound { frameCount = (uint)_nextSoundId };
        }
    }
}
