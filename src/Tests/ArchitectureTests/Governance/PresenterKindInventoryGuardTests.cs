using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Architecture.Governance
{
    [Category("ci-gate")]
    [Category("arch-guard")]
    public sealed class PresenterKindInventoryGuardTests
    {
        [Test]
        public void AssetKind_AllowlistMatchesEpicInventory()
        {
            string[] expected =
            {
                nameof(AssetKind.Mesh),
                nameof(AssetKind.SkinnedMesh),
                nameof(AssetKind.Decal),
                nameof(AssetKind.VFX),
                nameof(AssetKind.Sound),
                nameof(AssetKind.Spline),
                nameof(AssetKind.WorldHud),
                nameof(AssetKind.WorldText),
                nameof(AssetKind.GroundOverlay),
                nameof(AssetKind.Surface),
            };

            AssertKindAllowlist(
                typeof(AssetKind),
                expected,
                "AssetKind allowlist is frozen by Epic #924 P0. Adding a kind requires updating that Epic inventory first.");
            Assert.That((byte)AssetKind.Mesh, Is.EqualTo(1));
            Assert.That((byte)AssetKind.SkinnedMesh, Is.EqualTo(2));
            Assert.That((byte)AssetKind.Decal, Is.EqualTo(3));
            Assert.That((byte)AssetKind.VFX, Is.EqualTo(4));
            Assert.That((byte)AssetKind.Sound, Is.EqualTo(5));
            Assert.That((byte)AssetKind.Spline, Is.EqualTo(6));
            Assert.That((byte)AssetKind.WorldHud, Is.EqualTo(7));
            Assert.That((byte)AssetKind.WorldText, Is.EqualTo(8));
            Assert.That((byte)AssetKind.GroundOverlay, Is.EqualTo(9));
            Assert.That((byte)AssetKind.Surface, Is.EqualTo(10));
        }

        [Test]
        public void BehaviorKind_AllowlistMatchesEpicInventory()
        {
            string[] expected =
            {
                nameof(BehaviorKind.None),
                nameof(BehaviorKind.AssetBinding),
                nameof(BehaviorKind.AttributeBinding),
                nameof(BehaviorKind.TagBinding),
                nameof(BehaviorKind.Animator),
                nameof(BehaviorKind.Attachment),
                nameof(BehaviorKind.Sound),
                nameof(BehaviorKind.Material),
                nameof(BehaviorKind.Spline),
                nameof(BehaviorKind.Grounding),
                nameof(BehaviorKind.MinimapMarker),
                nameof(BehaviorKind.WorldText),
                nameof(BehaviorKind.SurfaceSource),
                nameof(BehaviorKind.InstancedBatch),
                nameof(BehaviorKind.TrailMesh),
                nameof(BehaviorKind.ScreenRect),
                nameof(BehaviorKind.Extension),
            };

            AssertKindAllowlist(
                typeof(BehaviorKind),
                expected,
                "BehaviorKind allowlist is frozen by Epic #924 P0. Adding a kind requires updating that Epic inventory first.");
            Assert.That((byte)BehaviorKind.AssetBinding, Is.EqualTo(1));
            Assert.That((byte)BehaviorKind.AttributeBinding, Is.EqualTo(2));
            Assert.That((byte)BehaviorKind.TagBinding, Is.EqualTo(3));
            Assert.That((byte)BehaviorKind.Animator, Is.EqualTo(4));
            Assert.That((byte)BehaviorKind.Attachment, Is.EqualTo(5));
            Assert.That((byte)BehaviorKind.Sound, Is.EqualTo(6));
            Assert.That((byte)BehaviorKind.Material, Is.EqualTo(7));
            Assert.That((byte)BehaviorKind.Spline, Is.EqualTo(8));
            Assert.That((byte)BehaviorKind.Grounding, Is.EqualTo(9));
            Assert.That((byte)BehaviorKind.MinimapMarker, Is.EqualTo(10));
            Assert.That((byte)BehaviorKind.WorldText, Is.EqualTo(11));
            Assert.That((byte)BehaviorKind.SurfaceSource, Is.EqualTo(12));
            Assert.That((byte)BehaviorKind.InstancedBatch, Is.EqualTo(13));
            Assert.That((byte)BehaviorKind.TrailMesh, Is.EqualTo(14));
            Assert.That((byte)BehaviorKind.ScreenRect, Is.EqualTo(15));
            Assert.That((byte)BehaviorKind.Extension, Is.EqualTo(255));
        }

        [Test]
        public void PresentationRequestKind_AllowlistMatchesEpicInventory()
        {
            // Prefab was deleted from this allowlist; do not reintroduce it.
            // Spline ribbon kinds land as P4 (#929) already merged into atmosphere.
            string[] expected =
            {
                nameof(PresentationRequestKind.VisualProxy),
                nameof(PresentationRequestKind.GroundOverlay),
                nameof(PresentationRequestKind.WorldHud),
                nameof(PresentationRequestKind.SplineRibbon),
                nameof(PresentationRequestKind.SurfaceSource),
                nameof(PresentationRequestKind.RemoveGroundOverlay),
                nameof(PresentationRequestKind.RemoveWorldHud),
                nameof(PresentationRequestKind.RemoveSplineRibbon),
                nameof(PresentationRequestKind.RemoveSurfaceSource),
                nameof(PresentationRequestKind.ClearTransientVisualProjection),
            };

            AssertKindAllowlist(
                typeof(PresentationRequestKind),
                expected,
                "PresentationRequestKind allowlist is frozen by Epic #924. Adding a kind requires updating that Epic inventory first.");
            Assert.That((byte)PresentationRequestKind.VisualProxy, Is.EqualTo(1));
            Assert.That((byte)PresentationRequestKind.GroundOverlay, Is.EqualTo(3));
            Assert.That((byte)PresentationRequestKind.WorldHud, Is.EqualTo(4));
            Assert.That((byte)PresentationRequestKind.SplineRibbon, Is.EqualTo(5));
            Assert.That((byte)PresentationRequestKind.SurfaceSource, Is.EqualTo(6));
            Assert.That((byte)PresentationRequestKind.RemoveGroundOverlay, Is.EqualTo(7));
            Assert.That((byte)PresentationRequestKind.RemoveWorldHud, Is.EqualTo(8));
            Assert.That((byte)PresentationRequestKind.RemoveSplineRibbon, Is.EqualTo(9));
            Assert.That((byte)PresentationRequestKind.RemoveSurfaceSource, Is.EqualTo(10));
            Assert.That((byte)PresentationRequestKind.ClearTransientVisualProjection, Is.EqualTo(11));
        }

        [Test]
        public void ProductionPresentationPath_MustNotCallPrefabBypass()
        {
            string repoRoot = FindRepoRoot();
            string[] directories =
            {
                Path.Combine(repoRoot, "src", "Core"),
                Path.Combine(repoRoot, "src", "Client"),
                Path.Combine(repoRoot, "mods"),
            };
            string[] forbidden =
            {
                "TryAddPrefab(",
                "TryAddAnchoredPrefab(",
                "PresentationRequest.FromPrefab(",
                "PrefabFinalizationPipeline.FinalizeVisuals(",
                "FinalizeVisuals(",
                "PresentationRequestKind.Prefab",
            };

            var hits = new List<string>();
            foreach (string dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                    string[] lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        for (int f = 0; f < forbidden.Length; f++)
                        {
                            if (line.Contains(forbidden[f], StringComparison.Ordinal))
                            {
                                hits.Add($"{relative}:{i + 1}: {line.Trim()}");
                                break;
                            }
                        }
                    }
                }
            }

            if (hits.Count > 0)
            {
                Assert.Fail("Production presentation path still uses Prefab bypass:\n" + string.Join("\n", hits));
            }
        }

        [Test]
        public void PrefabStackTypes_MustNotExist()
        {
            Assembly core = typeof(MeshAssetRegistry).Assembly;
            string[] forbiddenTypeNames =
            {
                "Ludots.Core.Presentation.Assets.PrefabRegistry",
                "Ludots.Core.Presentation.Assets.PrefabFinalizationPipeline",
                "Ludots.Core.Presentation.Assets.PrefabPart",
                "Ludots.Core.Presentation.Assets.PrefabDefinition",
                "Ludots.Core.Presentation.Assets.PrefabFinalizedVisual",
                "Ludots.Core.Presentation.Assets.WellKnownPrefabKeys",
                "Ludots.Core.Presentation.Assets.PresentationBehaviorRegistry",
                "Ludots.Core.Presentation.Primitives.WellKnownPrefabKeys",
            };

            var hits = new List<string>();
            for (int i = 0; i < forbiddenTypeNames.Length; i++)
            {
                Type? type = core.GetType(forbiddenTypeNames[i], throwOnError: false);
                if (type != null)
                {
                    hits.Add(forbiddenTypeNames[i]);
                }
            }

            string[] requestKinds = Enum.GetNames(typeof(PresentationRequestKind));
            if (requestKinds.Contains("Prefab", StringComparer.Ordinal))
            {
                hits.Add("PresentationRequestKind.Prefab");
            }

            string[] meshTypes = Enum.GetNames(typeof(MeshAssetType));
            if (meshTypes.Contains("Prefab", StringComparer.Ordinal))
            {
                hits.Add("MeshAssetType.Prefab");
            }

            FieldInfo? prefabRegistryKey = typeof(Ludots.Core.Scripting.CoreServiceKeys).GetField(
                "PresentationPrefabRegistry",
                BindingFlags.Public | BindingFlags.Static);
            if (prefabRegistryKey != null)
            {
                hits.Add("CoreServiceKeys.PresentationPrefabRegistry");
            }

            Assert.That(hits, Is.Empty, "Prefab stack types must not exist:\n" + string.Join("\n", hits));
        }

        [Test]
        public void BusinessPrefabJson_MustNotExist()
        {
            string repoRoot = FindRepoRoot();
            string[] directories =
            {
                Path.Combine(repoRoot, "mods"),
                Path.Combine(repoRoot, "assets"),
            };

            var hits = new List<string>();
            foreach (string dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(dir, "prefabs.json", SearchOption.AllDirectories))
                {
                    hits.Add(Path.GetRelativePath(repoRoot, file).Replace('\\', '/'));
                }
            }

            Assert.That(hits, Is.Empty, "Business prefabs.json must not exist:\n" + string.Join("\n", hits));
        }

        private static void AssertKindAllowlist(Type enumType, string[] expected, string failMessage)
        {
            string[] actual = Enum.GetNames(enumType);
            Assert.That(
                actual.Length,
                Is.EqualTo(expected.Length),
                $"{failMessage} {enumType.Name} has {actual.Length} values [{string.Join(", ", actual)}] but the frozen allowlist has {expected.Length} [{string.Join(", ", expected)}].");
            Assert.That(
                actual,
                Is.EqualTo(expected),
                failMessage);
            Assert.That(
                actual.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(actual.Length),
                $"{enumType.Name} must not duplicate names.");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "assets")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }
    }
}
