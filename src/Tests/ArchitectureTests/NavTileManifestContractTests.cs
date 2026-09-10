using System;
using System.IO;
using Ludots.Core.Navigation.NavMesh;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// NavTile 产物清单合同（issue #1346 / M2）：清单必须能证明"这批瓦片属于哪张地图、
    /// 哪块板、哪个格式版本、哪次源输入"，并且冷启动读取要先校验再发布。
    /// 写入时间不进入内容指纹，相同输入必须得到相同 buildHash。
    /// </summary>
    [TestFixture]
    public sealed class NavTileManifestContractTests
    {
        private string _tempRoot = null!;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "ludots-nav-manifest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
        }

        [Test]
        public void WriteRead_RoundTripsIdentityAndTiles()
        {
            string path = Path.Combine(_tempRoot, "navtiles.manifest.json");
            NavTileManifest manifest = CreateManifest();

            NavTileManifestSerializer.Write(path, manifest);
            NavTileManifest read = NavTileManifestSerializer.Read(path);

            Assert.That(read.MapId, Is.EqualTo("coastline"));
            Assert.That(read.BoardId, Is.EqualTo("mainland"));
            Assert.That(read.FormatVersion, Is.EqualTo(NavTileBinary.FormatVersion));
            Assert.That(read.SourceRevision, Is.EqualTo("file:fnv1a64:deadbeef:4096"));
            Assert.That(read.Tiles.Length, Is.EqualTo(2));
            Assert.That(read.BuildHash, Is.EqualTo(manifest.BuildHash));
        }

        [Test]
        public void BuildHash_IsStableAcrossWriteTime()
        {
            string first = Path.Combine(_tempRoot, "a.json");
            string second = Path.Combine(_tempRoot, "b.json");

            NavTileManifest a = CreateManifest();
            a.WrittenUtc = "2026-01-01T00:00:00.0000000Z";
            NavTileManifestSerializer.Write(first, a);

            NavTileManifest b = CreateManifest();
            b.WrittenUtc = "2030-12-31T23:59:59.0000000Z";
            NavTileManifestSerializer.Write(second, b);

            Assert.That(b.BuildHash, Is.EqualTo(a.BuildHash),
                "wall-clock write time must not change the deterministic build hash");
        }

        [Test]
        public void BuildHash_IsStableAcrossTileOrdering()
        {
            NavTileManifest ordered = CreateManifest();
            ordered.BuildHash = ordered.ComputeBuildHash();

            NavTileManifest reversed = CreateManifest();
            Array.Reverse(reversed.Tiles);
            reversed.BuildHash = reversed.ComputeBuildHash();

            Assert.That(reversed.BuildHash, Is.EqualTo(ordered.BuildHash),
                "tile enumeration order must not change the build hash");
        }

        [Test]
        public void BuildHash_ChangesWhenSourceRevisionChanges()
        {
            NavTileManifest baseline = CreateManifest();
            baseline.BuildHash = baseline.ComputeBuildHash();

            NavTileManifest changed = CreateManifest();
            changed.SourceRevision = "file:fnv1a64:feedface:4096";
            changed.BuildHash = changed.ComputeBuildHash();

            Assert.That(changed.BuildHash, Is.Not.EqualTo(baseline.BuildHash),
                "a different source input must be visible in the build hash");
        }

        [Test]
        public void BuildHash_ChangesWhenTileChecksumChanges()
        {
            NavTileManifest baseline = CreateManifest();
            baseline.BuildHash = baseline.ComputeBuildHash();

            NavTileManifest changed = CreateManifest();
            changed.Tiles[0].TileChecksum = "fnv1a64:0000000000000001";
            changed.BuildHash = changed.ComputeBuildHash();

            Assert.That(changed.BuildHash, Is.Not.EqualTo(baseline.BuildHash));
        }

        [Test]
        public void Read_MissingFile_ReportsRebakeAction()
        {
            string missing = Path.Combine(_tempRoot, "nope.json");

            Assert.That(
                () => NavTileManifestSerializer.Read(missing),
                Throws.TypeOf<FileNotFoundException>().With.Message.Contains("Re-run the nav bake"));
        }

        [Test]
        public void Read_TamperedBuildHash_FailsClosed()
        {
            string path = Path.Combine(_tempRoot, "tampered.json");
            NavTileManifestSerializer.Write(path, CreateManifest());

            string json = File.ReadAllText(path);
            int index = json.IndexOf("\"buildHash\":", StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThan(0));
            json = json.Replace("\"buildHash\": \"", "\"buildHash\": \"x");
            File.WriteAllText(path, json);

            Assert.That(
                () => NavTileManifestSerializer.Read(path),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("tampered or stale"));
        }

        [Test]
        public void Read_UnsupportedFormatVersion_FailsClosed()
        {
            string path = Path.Combine(_tempRoot, "oldformat.json");
            NavTileManifest manifest = CreateManifest();
            manifest.FormatVersion = NavTileBinary.FormatVersion + 1;
            NavTileManifestSerializer.Write(path, manifest);

            Assert.That(
                () => NavTileManifestSerializer.Read(path),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("Re-bake"),
                "a format the runtime cannot read must never be served silently");
        }

        [Test]
        public void ManifestPath_SitsUnderMapNavRoot()
        {
            Assert.That(
                NavAssetPaths.GetNavTileManifestRelativePath("coastline"),
                Is.EqualTo("assets/Data/Nav/coastline/navtiles.manifest.json"));
        }

        [Test]
        public void StoreWithManifest_RejectsTileWhoseChecksumDiffers()
        {
            var tile = DefaultGridNavTileFactory.CreateFlatTile(0, 0, layer: 0, tileVersion: 1, chunkSizeCells: 64, cellSizeCm: 250);
            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, tile);
            byte[] blob = ms.ToArray();

            var manifest = new NavTileManifest
            {
                MapId = "m",
                Tiles = new[]
                {
                    new NavTileManifestEntry
                    {
                        Layer = 0,
                        ProfileId = "p",
                        ChunkX = 0,
                        ChunkY = 0,
                        TileVersion = 1,
                        TileChecksum = "fnv1a64:0000000000000000"
                    }
                }
            };
            manifest.BuildHash = manifest.ComputeBuildHash();

            var store = new NavTileStore(_ => new MemoryStream(blob, writable: false), manifest);

            Assert.That(
                () => store.GetOrLoad(new NavTileId(0, 0, 0)),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("does not match the manifest"));
        }

        [Test]
        public void StoreWithManifest_RejectsTileMissingFromManifest()
        {
            var tile = DefaultGridNavTileFactory.CreateFlatTile(0, 0, layer: 0, tileVersion: 1, chunkSizeCells: 64, cellSizeCm: 250);
            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, tile);
            byte[] blob = ms.ToArray();

            var manifest = new NavTileManifest { MapId = "m", Tiles = Array.Empty<NavTileManifestEntry>() };
            manifest.BuildHash = manifest.ComputeBuildHash();
            var store = new NavTileStore(_ => new MemoryStream(blob, writable: false), manifest);

            Assert.That(
                () => store.GetOrLoad(new NavTileId(0, 0, 0)),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("not listed in the map's manifest"));
        }

        [Test]
        public void StoreWithMatchingManifest_LoadsNormally()
        {
            var tile = DefaultGridNavTileFactory.CreateFlatTile(0, 0, layer: 0, tileVersion: 7, chunkSizeCells: 64, cellSizeCm: 250);
            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, tile);
            byte[] blob = ms.ToArray();

            var manifest = new NavTileManifest
            {
                MapId = "m",
                Tiles = new[]
                {
                    new NavTileManifestEntry
                    {
                        Layer = 0,
                        ProfileId = "p",
                        ChunkX = 0,
                        ChunkY = 0,
                        TileVersion = 7,
                        TileChecksum = "fnv1a64:" + PersistedChecksum(blob).ToString("x16")
                    }
                }
            };
            manifest.BuildHash = manifest.ComputeBuildHash();
            var store = new NavTileStore(_ => new MemoryStream(blob, writable: false), manifest);

            Assert.That(store.GetOrLoad(new NavTileId(0, 0, 0)).TileId, Is.EqualTo(new NavTileId(0, 0, 0)));
        }

        private static ulong PersistedChecksum(byte[] blob)
        {
            using var ms = new MemoryStream(blob, writable: false);
            return NavTileBinary.Read(ms).Checksum;
        }

        [Test]
        public void Write_DuplicateFullIdentity_FailsFast()
        {
            string path = Path.Combine(_tempRoot, "dup.json");
            NavTileManifest manifest = CreateManifest();
            manifest.Tiles = new[]
            {
                manifest.Tiles[0],
                manifest.Tiles[0] // same layer+profile+coord, listed twice
            };

            Assert.That(
                () => NavTileManifestSerializer.Write(path, manifest),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("more than once"));
        }

        [Test]
        public void Write_NullTileList_FailsFast()
        {
            NavTileManifest manifest = CreateManifest();
            manifest.Tiles = null;

            Assert.That(
                () => NavTileManifestSerializer.Write(Path.Combine(_tempRoot, "null.json"), manifest),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("null tile list"));
        }

        [Test]
        public void Write_EmptyTileList_FailsFast()
        {
            NavTileManifest manifest = CreateManifest();
            manifest.Tiles = Array.Empty<NavTileManifestEntry>();

            Assert.That(
                () => NavTileManifestSerializer.Write(Path.Combine(_tempRoot, "empty.json"), manifest),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("zero tiles"));
        }

        [Test]
        public void Write_BlankProfileId_FailsFast()
        {
            NavTileManifest manifest = CreateManifest();
            manifest.Tiles = new[]
            {
                new NavTileManifestEntry
                {
                    Layer = 0,
                    ProfileId = string.Empty,
                    ChunkX = 0,
                    ChunkY = 0,
                    TileVersion = 1,
                    TileChecksum = "fnv1a64:1111111111111111"
                }
            };

            Assert.That(
                () => NavTileManifestSerializer.Write(Path.Combine(_tempRoot, "blank.json"), manifest),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("no profileId"));
        }

        [Test]
        public void Read_NullTilesInJson_FailsFast()
        {
            string path = Path.Combine(_tempRoot, "nulltiles.json");
            NavTileManifestSerializer.Write(path, CreateManifest());

            var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            doc["tiles"] = null;
            File.WriteAllText(path, doc.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            Assert.That(
                () => NavTileManifestSerializer.Read(path),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("null"));
        }

        [Test]
        public void ManifestPath_BoardScoped_KeepsPerBoardSegments()
        {
            string mainland = NavAssetPaths.GetNavTileManifestRelativePath("coastline", "mainland");
            string harbor = NavAssetPaths.GetNavTileManifestRelativePath("coastline", "harbor");

            Assert.That(mainland, Is.EqualTo("assets/Data/Nav/coastline/board_mainland/navtiles.manifest.json"));
            Assert.That(harbor, Is.EqualTo("assets/Data/Nav/coastline/board_harbor/navtiles.manifest.json"));
            Assert.That(mainland, Is.Not.EqualTo(harbor));
        }

        private static NavTileManifest CreateManifest()
            => new()
            {
                MapId = "coastline",
                BoardId = "mainland",
                SourceRevision = "file:fnv1a64:deadbeef:4096",
                Algorithm = "Recast",
                Mode = "Offline",
                TileVersion = 3,
                Tiles = new[]
                {
                    new NavTileManifestEntry
                    {
                        Layer = 0,
                        ProfileId = "infantry",
                        ChunkX = 0,
                        ChunkY = 0,
                        TileVersion = 3,
                        TileChecksum = "fnv1a64:1111111111111111"
                    },
                    new NavTileManifestEntry
                    {
                        Layer = 0,
                        ProfileId = "infantry",
                        ChunkX = 1,
                        ChunkY = 0,
                        TileVersion = 3,
                        TileChecksum = "fnv1a64:2222222222222222"
                    }
                }
            };
    }
}
