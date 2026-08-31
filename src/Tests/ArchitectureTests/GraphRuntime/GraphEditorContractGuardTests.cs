using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                GraphAuthoringSugar.BtLeaf,
                GraphAuthoringSugar.FsmAction,
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
