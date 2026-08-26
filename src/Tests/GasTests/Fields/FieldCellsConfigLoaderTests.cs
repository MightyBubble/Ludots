using System;
using System.IO;
using System.Linq;
using Ludots.Core.Config;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class FieldCellsConfigLoaderTests
    {
        [Test]
        public void Load_MergesFragments_WithOrdinalSortedRegionIds()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCells(root, "Core", """
                {
                  "schemaVersion": 1,
                  "layer": "layerX",
                  "regions": [ "r2", "r1" ],
                  "cells": [ [1, 1, 2], [2, 2, 1] ]
                }
                """);
                WriteCells(root, "ModA", """
                {
                  "schemaVersion": 1,
                  "layer": "layerX",
                  "regions": [ "r3" ],
                  "cells": [ [5, 5, 1] ]
                }
                """);

                FieldCellsAsset asset = CreateLoader(root).Load("layerX")!;

                Assert.That(asset.RegionKeys, Is.EqualTo(new[] { "r1", "r2", "r3" }),
                    "region ids come from the Ordinal-sorted key union");
                Assert.That(asset.Points.Length, Is.EqualTo(3));
                Assert.That(
                    asset.Points.Select(c => c.RegionKey),
                    Is.EquivalentTo(new[] { "r2", "r1", "r3" }));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_SchemaV2_Rects_DoNotExpandIntoPoints()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCells(root, "Core", """
                {
                  "schemaVersion": 2,
                  "layer": "layerX",
                  "regions": [ "west", "east" ],
                  "rects": [ [0, 0, 99, 99, 2], [100, 0, 199, 99, 1] ]
                }
                """);

                FieldCellsAsset asset = CreateLoader(root).Load("layerX")!;

                Assert.That(asset.RegionKeys, Is.EqualTo(new[] { "east", "west" }));
                Assert.That(asset.Rects.Length, Is.EqualTo(2));
                Assert.That(asset.Points, Is.Empty);
                Assert.That(asset.Rects[0].RegionKey, Is.EqualTo("west"));
                Assert.That(asset.Rects[0].X1 - asset.Rects[0].X0 + 1, Is.EqualTo(100));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_SameCellSameRegion_AcrossFragments_IsIdempotent()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCells(root, "Core", """
                { "schemaVersion": 1, "layer": "layerX", "regions": [ "r1" ], "cells": [ [3, 3, 1] ] }
                """);
                WriteCells(root, "ModA", """
                { "schemaVersion": 1, "layer": "layerX", "regions": [ "r1", "r2" ], "cells": [ [3, 3, 1], [4, 4, 2] ] }
                """);

                FieldCellsAsset asset = CreateLoader(root).Load("layerX")!;

                Assert.That(asset.Points.Length, Is.EqualTo(2), "same-key duplicates collapse");
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_SameCellDifferentRegion_FailsClosed()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCells(root, "Core", """
                { "schemaVersion": 1, "layer": "layerX", "regions": [ "r1" ], "cells": [ [12, 7, 1] ] }
                """);
                WriteCells(root, "ModA", """
                { "schemaVersion": 1, "layer": "layerX", "regions": [ "r2" ], "cells": [ [12, 7, 1] ] }
                """);

                var exception = Assert.Throws<InvalidOperationException>(() => CreateLoader(root).Load("layerX"));
                Assert.That(exception!.Message, Does.Contain("(12,7)"), "error names the cell");
                Assert.That(exception.Message, Does.Contain("r1"));
                Assert.That(exception.Message, Does.Contain("r2"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_OverlappingRectsDifferentRegion_FailsClosed()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCells(root, "Core", """
                { "schemaVersion": 2, "layer": "layerX", "regions": [ "r1" ], "rects": [ [0, 0, 10, 10, 1] ] }
                """);
                WriteCells(root, "ModA", """
                { "schemaVersion": 2, "layer": "layerX", "regions": [ "r2" ], "rects": [ [5, 5, 15, 15, 1] ] }
                """);

                var exception = Assert.Throws<InvalidOperationException>(() => CreateLoader(root).Load("layerX"));
                Assert.That(exception!.Message, Does.Contain("overlapping"));
                Assert.That(exception.Message, Does.Contain("r1"));
                Assert.That(exception.Message, Does.Contain("r2"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_SortOrderIsStable_RegardlessOfFragmentOrder()
        {
            string rootA = CreateTempRoot();
            string rootB = CreateTempRoot();
            try
            {
                string fragmentOne = """
                { "schemaVersion": 1, "layer": "layerX", "regions": [ "b", "a" ], "cells": [ [0, 0, 1] ] }
                """;
                string fragmentTwo = """
                { "schemaVersion": 1, "layer": "layerX", "regions": [ "c" ], "cells": [ [0, 1, 1] ] }
                """;

                WriteCells(rootA, "Core", fragmentOne);
                WriteCells(rootA, "ModA", fragmentTwo);
                WriteCells(rootB, "Core", fragmentTwo);
                WriteCells(rootB, "ModA", fragmentOne);

                FieldCellsAsset first = CreateLoader(rootA).Load("layerX")!;
                FieldCellsAsset second = CreateLoader(rootB).Load("layerX")!;

                Assert.That(first.RegionKeys, Is.EqualTo(second.RegionKeys));
                Assert.That(first.Points.Select(c => c.RegionKey).OrderBy(k => k, StringComparer.Ordinal),
                    Is.EqualTo(second.Points.Select(c => c.RegionKey).OrderBy(k => k, StringComparer.Ordinal)));
            }
            finally
            {
                TryDeleteDirectory(rootA);
                TryDeleteDirectory(rootB);
            }
        }

        [Test]
        public void Load_MissingFile_ReturnsNull()
        {
            string root = CreateTempRoot();
            try
            {
                Assert.That(CreateLoader(root).Load("layerX"), Is.Null);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [TestCase("""{ "schemaVersion": 3, "layer": "layerX", "regions": [ "r1" ], "rects": [] }""", "schemaVersion")]
        [TestCase("""{ "schemaVersion": 2, "layer": "layerX", "regions": [ "r1" ], "cells": [] }""", "forbids 'cells'")]
        [TestCase("""{ "schemaVersion": 1, "layer": "layerX", "regions": [ "r1" ], "rects": [] }""", "forbids 'rects'")]
        [TestCase("""{ "schemaVersion": 2, "layer": "layerX", "regions": [ "r1" ] }""", "requires 'rects'")]
        [TestCase("""{ "schemaVersion": 1, "layer": "layerY", "regions": [ "r1" ], "cells": [] }""", "layerY")]
        [TestCase("""{ "schemaVersion": 1, "layer": "layerX", "regions": [], "cells": [] }""", "regions")]
        [TestCase("""{ "schemaVersion": 1, "layer": "layerX", "regions": [ "r1", "r1" ], "cells": [] }""", "duplicate region key 'r1'")]
        [TestCase("""{ "schemaVersion": 1, "layer": "layerX", "regions": [ " r1" ], "cells": [] }""", "whitespace")]
        [TestCase("""{ "schemaVersion": 1, "layer": "layerX", "regions": [ "r1" ], "cells": [ [1, 2, 2] ] }""", "regionId 2")]
        [TestCase("""{ "schemaVersion": 1, "layer": "layerX", "regions": [ "r1" ], "cells": [ [1, 2] ] }""", "exactly 3")]
        [TestCase("""{ "schemaVersion": 1, "layer": "layerX", "regions": [ "r1" ], "cells": [ [1, 2, "x"] ] }""", "integers")]
        [TestCase("""{ "schemaVersion": 1, "layer": "layerX", "regions": [ "r1" ], "cells": {}, "surprise": 1 }""", "surprise")]
        public void Load_RejectsMalformedAssets(string json, string expectedMessagePart)
        {
            string root = CreateTempRoot();
            try
            {
                WriteCells(root, "Core", json);
                var exception = Assert.Throws<InvalidOperationException>(() => CreateLoader(root).Load("layerX"));
                Assert.That(exception!.Message, Does.Contain(expectedMessagePart));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static FieldCellsConfigLoader CreateLoader(string root)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(root, "core"));
            vfs.Mount("ModA", Path.Combine(root, "modA"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add("ModA");
            return new FieldCellsConfigLoader(new ConfigPipeline(vfs, modLoader));
        }

        private static void WriteCells(string root, string source, string json)
        {
            string dir = source == "Core"
                ? Path.Combine(root, "core", "Fields", "cells")
                : Path.Combine(root, "modA", "assets", "Fields", "cells");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "layerX.json"), json);
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_FieldCellsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "core"));
            Directory.CreateDirectory(Path.Combine(root, "modA"));
            return root;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
