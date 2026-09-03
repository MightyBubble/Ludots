using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.GraphRuntime
{
    [Category("ci-gate")]
    [Category("arch-guard")]
    public sealed class GraphEditorContractGuardTests
    {
        [Test]
        public void ReactEditor_MustNotHardcodeAuthoringSugarOps()
        {
            string repoRoot = FindRepoRoot();
            string editorPath = Path.Combine(
                repoRoot,
                "src",
                "Tools",
                "Ludots.Editor.React",
                "src",
                "pages",
                "GasGraphEditorPage.tsx");
            Assert.That(File.Exists(editorPath), Is.True, $"Missing {editorPath}");

            string source = File.ReadAllText(editorPath);
            string[] forbiddenLiteralLists =
            {
                "['BranchBool'",
                "[\"BranchBool\"",
                "['SwitchInt'",
                "[\"SwitchInt\"",
                "['Wait'",
                "[\"Wait\"",
                "['While'",
                "[\"While\"",
                "['Until'",
                "[\"Until\"",
                "['Break'",
                "[\"Break\"",
            };

            var hits = new List<string>();
            foreach (string needle in forbiddenLiteralLists)
            {
                if (source.Contains(needle, StringComparison.Ordinal))
                {
                    hits.Add(needle);
                }
            }

            Assert.That(hits, Is.Empty,
                "GasGraphEditorPage must not invent authoring sugar op lists; consume Bridge descriptors/authoringSugars.");
        }

        /// <summary>
        /// The editor is one tool for every mod. Naming a specific showcase graph, mod or
        /// node id in its sources means that mod's data has been copied out of its own
        /// files, so a rename degrades the editor silently instead of failing closed.
        /// Author-facing prose belongs in the mod's graph_editor.json annotations.
        /// </summary>
        [Test]
        public void ReactEditor_MustNotNameShowcaseGraphsOrMods()
        {
            string repoRoot = FindRepoRoot();
            string editorSrc = Path.Combine(repoRoot, "src", "Tools", "Ludots.Editor.React", "src");
            Assert.That(Directory.Exists(editorSrc), Is.True, $"Missing {editorSrc}");

            var showcaseGraphIds = new HashSet<string>(StringComparer.Ordinal);
            var showcaseModIds = new HashSet<string>(StringComparer.Ordinal);
            string modsRoot = Path.Combine(repoRoot, "mods");
            foreach (string graphsPath in Directory.EnumerateFiles(modsRoot, "graphs.json", SearchOption.AllDirectories))
            {
                if (graphsPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    graphsPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                JsonNode? parsed = JsonNode.Parse(File.ReadAllText(graphsPath));
                if (parsed is not JsonArray graphs) continue;
                for (int i = 0; i < graphs.Count; i++)
                {
                    if (graphs[i] is JsonObject graph &&
                        graph["id"]?.GetValue<string>() is { Length: > 0 } graphId)
                    {
                        showcaseGraphIds.Add(graphId);
                    }
                }
            }

            foreach (string modJson in Directory.EnumerateFiles(modsRoot, "mod.json", SearchOption.AllDirectories))
            {
                if (JsonNode.Parse(File.ReadAllText(modJson)) is JsonObject mod &&
                    mod["id"]?.GetValue<string>() is { Length: > 0 } modId)
                {
                    showcaseModIds.Add(modId);
                }
            }

            Assert.That(showcaseGraphIds, Is.Not.Empty, "no showcase graph ids discovered; the guard would be vacuous");

            var hits = new List<string>();
            foreach (string file in Directory.EnumerateFiles(editorSrc, "*.ts*", SearchOption.AllDirectories))
            {
                string relative = ToRepoRelativePath(repoRoot, file);
                string[] lines = File.ReadAllLines(file);
                for (int line = 0; line < lines.Length; line++)
                {
                    // A `const DEFAULT_*_ID = '<id>'` declaration picks which graph the page
                    // opens on when the URL supplies no selection. It names a target without
                    // copying that mod's data, and any ?mod= / ?graph= overrides it.
                    if (Regex.IsMatch(lines[line], @"^\s*const DEFAULT_\w*_ID\s*=\s*'[^']*';\s*$"))
                    {
                        continue;
                    }

                    foreach (string id in showcaseGraphIds.Concat(showcaseModIds))
                    {
                        if (lines[line].Contains($"'{id}'", StringComparison.Ordinal) ||
                            lines[line].Contains($"\"{id}\"", StringComparison.Ordinal))
                        {
                            hits.Add($"{relative}:{line + 1}: names '{id}'");
                        }
                    }
                }
            }

            Assert.That(hits, Is.Empty,
                "The graph editor must not name specific showcase graphs or mods; move author-facing "
                + "prose into that mod's assets/GAS/graph_editor.json annotations:\n"
                + string.Join("\n", hits));
        }

        [Test]
        public void ReactEditor_MustResolveControlPortsFromDescriptorOrFailClosed()
        {
            string repoRoot = FindRepoRoot();
            string editorPath = Path.Combine(
                repoRoot,
                "src",
                "Tools",
                "Ludots.Editor.React",
                "src",
                "pages",
                "GasGraphEditorPage.tsx");
            string source = File.ReadAllText(editorPath);

            Assert.That(source, Does.Contain("resolveControlOutputPorts"));
            Assert.That(source, Does.Contain("Descriptor missing for graph op"));
            Assert.That(source, Does.Contain("controlOutputPorts"));
            Assert.That(source, Does.Contain("Descriptor response is missing control output ports."));
            Assert.That(source, Does.Contain("Control edge '"));
            Assert.That(source, Does.Contain("is missing a source port."));
        }

        [Test]
        public void BridgeDescriptorProjection_ExposesRuntimeAuthoritativeSugars()
        {
            string repoRoot = FindRepoRoot();
            string bridgePath = Path.Combine(repoRoot, "src", "Tools", "Ludots.Editor.Bridge", "Program.cs");
            string source = File.ReadAllText(bridgePath);

            string[] required =
            {
                GraphAuthoringSugar.BranchBool,
                GraphAuthoringSugar.SwitchInt,
                GraphAuthoringSugar.SelectByEnum,
                GraphAuthoringSugar.FsmState,
                GraphAuthoringSugar.BtSequence,
                GraphAuthoringSugar.BtSelector,
                GraphAuthoringSugar.BtDecorator,
                GraphAuthoringSugar.Wait,
                GraphAuthoringSugar.While,
                GraphAuthoringSugar.Until,
                GraphAuthoringSugar.Break,
                "ControlOutputPorts",
                "controlOutputPorts",
                "childArms",
            };

            foreach (string token in required)
            {
                Assert.That(source, Does.Contain(token), $"Bridge descriptor projection missing '{token}'.");
            }
        }

        /// <summary>
        /// Saving a graph must run the same entry-shape guard the engine loads with, or the
        /// editor can write an entry the game refuses to mount. Annotations must be checked
        /// against the graph they describe so a node rename fails loudly.
        /// </summary>
        [Test]
        public void BridgeGraphWritePaths_FailClosedOnEntryShapeAndAnnotationTargets()
        {
            string repoRoot = FindRepoRoot();
            string bridgePath = Path.Combine(repoRoot, "src", "Tools", "Ludots.Editor.Bridge", "Program.cs");
            string source = File.ReadAllText(bridgePath);

            string[] required =
            {
                "RequireTriggerGraphEntryShape",
                "IsValidGraphEditorAnnotations",
                "TryValidateAnnotationTargets",
                "/api/graph/input-actions/{modId}",
            };

            foreach (string token in required)
            {
                Assert.That(source, Does.Contain(token), $"Bridge is missing '{token}'.");
            }

            Assert.That(
                Regex.Matches(source, @"TryValidateAnnotationTargets\(modRoot, graphId,").Count,
                Is.GreaterThanOrEqualTo(2),
                "Both the sidecar read and write paths must check annotation targets.");
        }

        [Test]
        public void GraphDebugTrace_DoesNotEmitNodeExitInProducers()
        {
            string repoRoot = FindRepoRoot();
            var producers = new[]
            {
                Path.Combine(repoRoot, "src", "Core", "NodeLibraries", "GASGraph", "GasGraphOpHandlerTable.cs"),
                Path.Combine(repoRoot, "src", "Core", "Gameplay", "MapTriggers", "TriggerGraphMountTrigger.cs"),
                Path.Combine(repoRoot, "src", "Core", "GraphRuntime", "GraphDebugTrace.cs"),
            };

            var hits = new List<string>();
            foreach (string path in producers)
            {
                Assert.That(File.Exists(path), Is.True, path);
                foreach (Match match in Regex.Matches(
                             File.ReadAllText(path),
                             @"RecordNode\([^\)]*GraphDebugTraceEvent\.NodeExit"))
                {
                    hits.Add($"{ToRepoRelativePath(repoRoot, path)}: {match.Value}");
                }
            }

            Assert.That(hits, Is.Empty,
                "Producers must not emit GraphDebugTraceEvent.NodeExit until a formal lifecycle contract exists:\n" +
                string.Join("\n", hits));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root.");
        }

        private static string ToRepoRelativePath(string repoRoot, string absolutePath)
            => Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');
    }
}
