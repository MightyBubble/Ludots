using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Ludots.Adapter.Raylib.Effekseer;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace Ludots.Adapter.Raylib.Tests
{
    [TestFixture]
    public sealed class RaylibEffekseerRuntimeTests
    {
        private const string EffekseerAssetDirectory = "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/Effekseer";
        private const string SpriteAssetFileName = "laser_sprite_impact_flash.efkefc";
        private const string SpriteResourcePath = "textures/laser_soft_disc.png";
        private const string ModelAssetFileName = "laser_model_energy_shard.efkefc";
        private const string ModelResourcePath = "models/energy_shard.efkmodel";

        private static string _testAssetPath = string.Empty;
        private static string _testAssetSha256 = string.Empty;
        private readonly List<string> _nativeAssetFixtureDirectories = new();

        [OneTimeSetUp]
        public void CreateTestAsset()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("raylib-effekseer-runtime-test-asset");
            _testAssetPath = Path.Combine(Path.GetTempPath(), $"ludots-emitter-{Guid.NewGuid():N}.efkefc");
            File.WriteAllBytes(_testAssetPath, bytes);
            _testAssetSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        [OneTimeTearDown]
        public void DeleteTestAsset()
        {
            if (File.Exists(_testAssetPath))
            {
                File.Delete(_testAssetPath);
            }
        }

        [TearDown]
        public void DeleteNativeAssetFixtures()
        {
            foreach (string directory in _nativeAssetFixtureDirectories)
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }

            _nativeAssetFixtureDirectories.Clear();
        }

        [Test]
        public void NativeLibrary_LoadsBundledBridgeAndValidatesAbiWithoutAWindow()
        {
            using var native = new EffekseerNativeLibrary();

            Assert.Pass();
        }

        [TestCase(
            SpriteAssetFileName,
            SpriteResourcePath,
            "C:/absolute/texture_test.png",
            (int)EffekseerNodeType.Sprite,
            "Color texture")]
        [TestCase(
            ModelAssetFileName,
            ModelResourcePath,
            "C:/absolute/fixture.efkmodel",
            (int)EffekseerNodeType.Model,
            "Model")]
        public void NativeValidator_RejectsAbsoluteExternalResourcePath(
            string assetFileName,
            string authoredResourcePath,
            string absoluteResourcePath,
            int nodeType,
            string resourceKind)
        {
            string assetPath = CreateNativeAssetFixture(assetFileName, authoredResourcePath, createResource: false, emptyResource: false);
            ReplaceUtf16Path(assetPath, authoredResourcePath, absoluteResourcePath);
            using var native = new EffekseerNativeLibrary();

            EffekseerNativeException exception = Assert.Throws<EffekseerNativeException>(
                () => native.ValidateAsset(assetPath, (EffekseerNodeType)nodeType))!;

            Assert.That(exception.Message, Does.Contain($"{resourceKind} resource path must be relative to the emitter asset."));
        }

        [TestCase(
            SpriteAssetFileName,
            SpriteResourcePath,
            "../outside/texture_tests.png",
            (int)EffekseerNodeType.Sprite,
            "Color texture")]
        [TestCase(
            ModelAssetFileName,
            ModelResourcePath,
            "../outside/fixtures.efkmodel",
            (int)EffekseerNodeType.Model,
            "Model")]
        public void NativeValidator_RejectsParentTraversalExternalResourcePath(
            string assetFileName,
            string authoredResourcePath,
            string traversalResourcePath,
            int nodeType,
            string resourceKind)
        {
            string assetPath = CreateNativeAssetFixture(assetFileName, authoredResourcePath, createResource: false, emptyResource: false);
            ReplaceUtf16Path(assetPath, authoredResourcePath, traversalResourcePath);
            using var native = new EffekseerNativeLibrary();

            EffekseerNativeException exception = Assert.Throws<EffekseerNativeException>(
                () => native.ValidateAsset(assetPath, (EffekseerNodeType)nodeType))!;

            Assert.That(exception.Message, Does.Contain($"{resourceKind} resource path cannot traverse outside the emitter asset directory."));
        }

        [TestCase(SpriteAssetFileName, SpriteResourcePath, (int)EffekseerNodeType.Sprite, "Color texture")]
        [TestCase(ModelAssetFileName, ModelResourcePath, (int)EffekseerNodeType.Model, "Model")]
        public void NativeValidator_RejectsMissingExternalResource(
            string assetFileName,
            string resourcePath,
            int nodeType,
            string resourceKind)
        {
            string assetPath = CreateNativeAssetFixture(assetFileName, resourcePath, createResource: false, emptyResource: false);
            using var native = new EffekseerNativeLibrary();

            EffekseerNativeException exception = Assert.Throws<EffekseerNativeException>(
                () => native.ValidateAsset(assetPath, (EffekseerNodeType)nodeType))!;

            Assert.That(exception.Message, Does.Contain($"{resourceKind} resource file does not exist."));
        }

        [TestCase(SpriteAssetFileName, SpriteResourcePath, (int)EffekseerNodeType.Sprite, "Color texture")]
        [TestCase(ModelAssetFileName, ModelResourcePath, (int)EffekseerNodeType.Model, "Model")]
        public void NativeValidator_RejectsEmptyExternalResource(
            string assetFileName,
            string resourcePath,
            int nodeType,
            string resourceKind)
        {
            string assetPath = CreateNativeAssetFixture(assetFileName, resourcePath, createResource: true, emptyResource: true);
            using var native = new EffekseerNativeLibrary();

            EffekseerNativeException exception = Assert.Throws<EffekseerNativeException>(
                () => native.ValidateAsset(assetPath, (EffekseerNodeType)nodeType))!;

            Assert.That(exception.Message, Does.Contain($"{resourceKind} resource file is empty or unreadable."));
        }

        [TestCase(AssetKind.SpriteEmitter, (int)EffekseerNodeType.Sprite)]
        [TestCase(AssetKind.RibbonEmitter, (int)EffekseerNodeType.Ribbon)]
        [TestCase(AssetKind.RingEmitter, (int)EffekseerNodeType.Ring)]
        [TestCase(AssetKind.ModelEmitter, (int)EffekseerNodeType.Model)]
        [TestCase(AssetKind.TrackEmitter, (int)EffekseerNodeType.Track)]
        public void CoreResolver_MapsConcreteKindAndVfsPath(
            AssetKind assetKind,
            int expectedNodeType)
        {
            const string assetKey = "test.emitter";
            var registry = new EmitterAssetRegistry();
            int assetId = registry.Register(
                assetKey,
                assetKind,
                EmitterAssetDescriptor.RequiredRuntimeFormatVersion,
                _testAssetSha256);
            string mountRoot = Path.GetDirectoryName(_testAssetPath)!;
            string sourceUri = $"Test:{Path.GetFileName(_testAssetPath)}";
            registry.BindSourceUris(assetKey, assetKind, new[] { sourceUri });
            var vfs = new VirtualFileSystem();
            vfs.Mount("Test", mountRoot);
            var resolver = new CoreRaylibEmitterSnapshotResolver(registry, vfs);
            var item = new PrimitiveDrawItem
            {
                StableId = 101,
                AssetId = assetId,
                AssetKind = assetKind,
                Position = Vector3.One,
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                Visibility = VisualVisibility.Visible,
            };

            bool resolved = resolver.TryResolve(in item, out RaylibEmitterSnapshotItem emitter);

            Assert.That(resolved, Is.True);
            Assert.That(emitter.NodeType, Is.EqualTo((EffekseerNodeType)expectedNodeType));
            Assert.That(emitter.FullPath, Is.EqualTo(Path.GetFullPath(_testAssetPath)).IgnoreCase);
            Assert.That(emitter.Sha256, Is.EqualTo(_testAssetSha256));
        }

        [Test]
        public void Sync_CulledEmitterRemainsAliveAndIsOnlyHidden()
        {
            var native = new FakeNativeApi();
            var resolver = new FakeResolver();
            resolver.Add(CreateEmitter(101, visibility: VisualVisibility.Culled));
            using var runtime = new RaylibEffekseerRuntime(resolver, native);
            PrimitiveDrawBuffer snapshot = Snapshot(101);

            runtime.Sync(snapshot);
            runtime.Update(1f / 60f);

            Assert.That(native.PlayCount, Is.EqualTo(1));
            Assert.That(native.StopCount, Is.Zero);
            Assert.That(native.LastShown, Is.False);
            Assert.That(runtime.InstanceCount, Is.EqualTo(1));
        }

        [Test]
        public void Sync_UntouchedStableIdStopsAndRemovesInstance()
        {
            var native = new FakeNativeApi();
            var resolver = new FakeResolver();
            resolver.Add(CreateEmitter(101));
            using var runtime = new RaylibEffekseerRuntime(resolver, native);

            runtime.Sync(Snapshot(101));
            runtime.Update(1f / 60f);
            runtime.Sync(new PrimitiveDrawBuffer());
            runtime.Update(1f / 60f);

            Assert.That(native.StopCount, Is.EqualTo(1));
            Assert.That(runtime.InstanceCount, Is.Zero);
        }

        [Test]
        public void Sync_NaturallyCompletedHandleIsNotReplayedWhileStableIdRemains()
        {
            var native = new FakeNativeApi();
            var resolver = new FakeResolver();
            resolver.Add(CreateEmitter(101));
            using var runtime = new RaylibEffekseerRuntime(resolver, native);
            PrimitiveDrawBuffer snapshot = Snapshot(101);

            runtime.Sync(snapshot);
            runtime.Update(1f / 60f);
            native.MarkAllCompleted();
            runtime.Sync(snapshot);
            runtime.Update(1f / 60f);
            runtime.Sync(snapshot);
            runtime.Update(1f / 60f);

            Assert.That(native.PlayCount, Is.EqualTo(1));
            Assert.That(native.TransformCount, Is.EqualTo(1));
            Assert.That(runtime.InstanceCount, Is.EqualTo(1));
        }

        [Test]
        public void Sync_AssetChangeStopsOldHandleAndStartsNewAsset()
        {
            var native = new FakeNativeApi();
            var resolver = new FakeResolver();
            resolver.Add(CreateEmitter(101, assetId: 11));
            using var runtime = new RaylibEffekseerRuntime(resolver, native);
            PrimitiveDrawBuffer snapshot = Snapshot(101);

            runtime.Sync(snapshot);
            runtime.Update(1f / 60f);
            resolver.Add(CreateEmitter(101, assetId: 12, nodeType: EffekseerNodeType.Track));
            runtime.Sync(snapshot);
            runtime.Update(1f / 60f);

            Assert.That(native.PlayCount, Is.EqualTo(2));
            Assert.That(native.StopCount, Is.EqualTo(1));
            Assert.That(native.LoadedAssetIds, Is.EquivalentTo(new[] { 11, 12 }));
        }

        [Test]
        public void Sync_DuplicateStableIdFailsExplicitly()
        {
            var native = new FakeNativeApi();
            var resolver = new FakeResolver();
            resolver.Add(CreateEmitter(101));
            using var runtime = new RaylibEffekseerRuntime(resolver, native);
            var snapshot = new PrimitiveDrawBuffer();
            snapshot.TryAdd(new PrimitiveDrawItem { StableId = 101 });
            snapshot.TryAdd(new PrimitiveDrawItem { StableId = 101 });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runtime.Sync(snapshot))!;

            Assert.That(exception.Message, Does.Contain("duplicate stableId=101"));
        }

        [Test]
        public void Update_SecondCallWithoutNewSyncFailsExplicitly()
        {
            var native = new FakeNativeApi();
            var resolver = new FakeResolver();
            using var runtime = new RaylibEffekseerRuntime(resolver, native);
            runtime.Sync(new PrimitiveDrawBuffer());
            runtime.Update(1f / 60f);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runtime.Update(1f / 60f))!;

            Assert.That(exception.Message, Does.Contain("only once"));
            Assert.That(native.UpdateCount, Is.EqualTo(1));
        }

        [Test]
        public void StableSyncAndUpdate_DoNotAllocateAfterWarmup()
        {
            var native = new FakeNativeApi();
            var resolver = new FakeResolver();
            resolver.Add(CreateEmitter(101));
            using var runtime = new RaylibEffekseerRuntime(resolver, native);
            PrimitiveDrawBuffer snapshot = Snapshot(101);
            runtime.Sync(snapshot);
            runtime.Update(1f / 60f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                runtime.Sync(snapshot);
                runtime.Update(1f / 60f);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Sync_VisibleEmitterAppliesTargetAndAllFourDynamicInputs()
        {
            var native = new FakeNativeApi();
            var resolver = new FakeResolver();
            resolver.Add(CreateEmitter(101) with
            {
                HasTarget = true,
                TargetPosition = new Vector3(9f, 8f, 7f),
                DynamicInputCount = 4,
                DynamicInput0 = 0.1f,
                DynamicInput1 = 0.2f,
                DynamicInput2 = 0.3f,
                DynamicInput3 = 0.4f,
            });
            using var runtime = new RaylibEffekseerRuntime(resolver, native);

            runtime.Sync(Snapshot(101));
            runtime.Update(1f / 60f);

            Assert.That(native.LastTarget, Is.EqualTo(new Vector3(9f, 8f, 7f)));
            Assert.That(native.DynamicInputs, Is.EqualTo(new[] { 0.1f, 0.2f, 0.3f, 0.4f }));
        }

        [Test]
        public void Sync_TargetedEmitterForwardsTargetWithoutRewritingItsAuthoredTransform()
        {
            var native = new FakeNativeApi();
            var resolver = new FakeResolver();
            resolver.Add(CreateEmitter(101, nodeType: EffekseerNodeType.Track) with
            {
                HasTarget = true,
                TargetPosition = new Vector3(6f, 2f, 3f),
                Scale = new Vector3(1.2f, 0.5f, 0.25f),
            });
            using var runtime = new RaylibEffekseerRuntime(resolver, native);

            runtime.Sync(Snapshot(101));
            runtime.Update(1f / 60f);

            Assert.That(native.LastTarget, Is.EqualTo(new Vector3(6f, 2f, 3f)));
            Assert.That(native.LastScale.X, Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(native.LastScale.Y, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(native.LastScale.Z, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(native.LastRotationRadians, Is.EqualTo(Vector3.Zero));
        }

        [Test]
        public void Sync_Sha256MismatchFailsBeforeNativeLoadAsset()
        {
            var native = new FakeNativeApi();
            var resolver = new FakeResolver();
            resolver.Add(CreateEmitter(101) with { Sha256 = new string('0', 64) });
            using var runtime = new RaylibEffekseerRuntime(resolver, native);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => runtime.Sync(Snapshot(101)))!;

            Assert.That(exception.Message, Does.Contain("SHA-256 mismatch"));
            Assert.That(exception.Message, Does.Contain("Native LoadAsset was not called"));
            Assert.That(native.LoadedAssetIds, Is.Empty);
            Assert.That(native.PlayCount, Is.Zero);
        }

        private static PrimitiveDrawBuffer Snapshot(params int[] stableIds)
        {
            var snapshot = new PrimitiveDrawBuffer(Math.Max(1, stableIds.Length));
            foreach (int stableId in stableIds)
            {
                snapshot.TryAdd(new PrimitiveDrawItem { StableId = stableId });
            }
            return snapshot;
        }

        private string CreateNativeAssetFixture(
            string assetFileName,
            string resourcePath,
            bool createResource,
            bool emptyResource)
        {
            string sourceDirectory = Path.Combine(FindRepoRoot(), EffekseerAssetDirectory);
            string fixtureDirectory = Path.Combine(Path.GetTempPath(), $"ludots-effekseer-contract-{Guid.NewGuid():N}");
            Directory.CreateDirectory(fixtureDirectory);
            _nativeAssetFixtureDirectories.Add(fixtureDirectory);

            string assetPath = Path.Combine(fixtureDirectory, assetFileName);
            File.Copy(Path.Combine(sourceDirectory, assetFileName), assetPath);

            if (createResource)
            {
                string fixtureResourcePath = Path.Combine(fixtureDirectory, resourcePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fixtureResourcePath)!);
                if (emptyResource)
                {
                    File.WriteAllBytes(fixtureResourcePath, Array.Empty<byte>());
                }
                else
                {
                    File.Copy(Path.Combine(sourceDirectory, resourcePath), fixtureResourcePath);
                }
            }

            return assetPath;
        }

        private static void ReplaceUtf16Path(string assetPath, string authoredPath, string replacementPath)
        {
            if (authoredPath.Length != replacementPath.Length)
            {
                throw new InvalidOperationException(
                    $"Effekseer fixture path replacement must preserve length: '{authoredPath}' -> '{replacementPath}'.");
            }

            byte[] bytes = File.ReadAllBytes(assetPath);
            byte[] authoredBytes = Encoding.Unicode.GetBytes(authoredPath);
            byte[] replacementBytes = Encoding.Unicode.GetBytes(replacementPath);
            var matchOffsets = new List<int>();
            for (int offset = 0; offset <= bytes.Length - authoredBytes.Length; offset++)
            {
                bool matches = true;
                for (int index = 0; index < authoredBytes.Length; index++)
                {
                    if (bytes[offset + index] != authoredBytes[index])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    matchOffsets.Add(offset);
                }
            }

            if (matchOffsets.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Expected UTF-16 path '{authoredPath}' in Effekseer fixture.");
            }

            foreach (int matchOffset in matchOffsets)
            {
                Buffer.BlockCopy(replacementBytes, 0, bytes, matchOffset, replacementBytes.Length);
            }

            File.WriteAllBytes(assetPath, bytes);
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                    Directory.Exists(Path.Combine(current.FullName, "src")) &&
                    Directory.Exists(Path.Combine(current.FullName, "mods")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
        }

        private static RaylibEmitterSnapshotItem CreateEmitter(
            int stableId,
            int assetId = 11,
            EffekseerNodeType nodeType = EffekseerNodeType.Sprite,
            VisualVisibility visibility = VisualVisibility.Visible)
        {
            return new RaylibEmitterSnapshotItem(
                stableId,
                assetId,
                nodeType,
                _testAssetPath,
                _testAssetSha256,
                new Vector3(1f, 2f, 3f),
                Quaternion.Identity,
                Vector3.One,
                Vector4.One,
                visibility,
                HasTarget: false,
                TargetPosition: default,
                DynamicInputCount: 0,
                DynamicInput0: 0f,
                DynamicInput1: 0f,
                DynamicInput2: 0f,
                DynamicInput3: 0f);
        }

        private sealed class FakeResolver : IRaylibEmitterSnapshotResolver
        {
            private readonly Dictionary<int, RaylibEmitterSnapshotItem> _items = new();

            public void Add(RaylibEmitterSnapshotItem item) => _items[item.StableId] = item;

            public bool TryResolve(in PrimitiveDrawItem item, out RaylibEmitterSnapshotItem emitter)
                => _items.TryGetValue(item.StableId, out emitter);
        }

        private sealed class FakeNativeApi : IEffekseerNativeApi
        {
            private readonly HashSet<int> _liveHandles = new();
            private int _nextHandle = 1;

            public int PlayCount { get; private set; }
            public int StopCount { get; private set; }
            public int TransformCount { get; private set; }
            public int UpdateCount { get; private set; }
            public bool LastShown { get; private set; }
            public Vector3 LastTarget { get; private set; }
            public Vector3 LastRotationRadians { get; private set; }
            public Vector3 LastScale { get; private set; }
            public List<int> LoadedAssetIds { get; } = new();
            public List<float> DynamicInputs { get; } = new();

            public IntPtr Create(int maxInstances, int maxSquares) => new(1);
            public void Destroy(IntPtr context) { }
            public void ValidateAsset(string fullPath, EffekseerNodeType expectedNodeType) { }
            public void LoadAsset(IntPtr context, int assetId, string fullPath, EffekseerNodeType expectedNodeType) => LoadedAssetIds.Add(assetId);
            public void UnloadAsset(IntPtr context, int assetId) { }

            public int Play(IntPtr context, int assetId, Vector3 position)
            {
                int handle = _nextHandle++;
                _liveHandles.Add(handle);
                PlayCount++;
                return handle;
            }

            public bool Exists(IntPtr context, int handle) => _liveHandles.Contains(handle);

            public void Stop(IntPtr context, int handle)
            {
                _liveHandles.Remove(handle);
                StopCount++;
            }

            public void SetTransform(IntPtr context, int handle, Vector3 position, Vector3 rotationRadians, Vector3 scale)
            {
                TransformCount++;
                LastRotationRadians = rotationRadians;
                LastScale = scale;
            }
            public void SetTarget(IntPtr context, int handle, Vector3 target) => LastTarget = target;
            public void SetColor(IntPtr context, int handle, byte red, byte green, byte blue, byte alpha) { }
            public void SetDynamicInput(IntPtr context, int handle, int index, float value) => DynamicInputs.Add(value);
            public void SetShown(IntPtr context, int handle, bool shown) => LastShown = shown;
            public void Update(IntPtr context, float deltaSeconds) => UpdateCount++;

            public void DrawPerspective(IntPtr context, Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUp, float verticalFovRadians, float aspect, float nearPlane, float farPlane) { }
            public void MarkAllCompleted() => _liveHandles.Clear();
            public void Dispose() { }
        }
    }
}
