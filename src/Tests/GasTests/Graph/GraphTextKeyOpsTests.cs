using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Registry;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphTextKeyOpsTests
    {
        [Test]
        public void LoadTextKey_WritesLocalizedTemplate_AndSinksSubtitle()
        {
            PresentationTextCatalog catalog = BuildCatalog(
                tokenKey: "gallery.hello",
                template: "你好");
            var sink = new GraphPresentationTextSink();
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindPresentationTextSink(sink);
            api.BindPresentationTextCatalog(catalog);

            GraphControlFlowDocument doc = ScriptDoc(
                "Graph.TextKey.Hello",
                nodes: new[]
                {
                    Node("a", "LoadTextKey", textKey: "gallery.hello"),
                    Node("sink", "SinkPresentationText", surface: "Subtitle"),
                    Node("halt", "HaltReturnInt"),
                },
                control: new[]
                {
                    Edge("a", "next", "sink"),
                    Edge("sink", "next", "halt"),
                },
                values: new[]
                {
                    Value("a", "value", "sink", "a"),
                });

            ExecuteScript(world, doc, api, catalog, out _, out _);

            That(sink.TryDequeue(out GraphPresentationTextSurface surface, out string text), Is.True);
            That(surface, Is.EqualTo(GraphPresentationTextSurface.Subtitle));
            That(text, Is.EqualTo("你好"));
        }

        [Test]
        public void LoadTextKey_ArgCountToken_FailsClosed()
        {
            PresentationTextCatalog catalog = BuildCatalog(
                tokenKey: "gallery.hp",
                template: "{0}",
                argCount: 1);
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindPresentationTextSink(new GraphPresentationTextSink());
            api.BindPresentationTextCatalog(catalog);

            GraphControlFlowDocument doc = ScriptDoc(
                "Graph.TextKey.Args",
                nodes: new[]
                {
                    Node("a", "LoadTextKey", textKey: "gallery.hp"),
                    Node("halt", "HaltReturnInt"),
                },
                control: new[] { Edge("a", "next", "halt") },
                values: Array.Empty<GraphControlFlowValueEdge>());

            var ex = Throws<InvalidOperationException>(() => ExecuteScript(world, doc, api, catalog, out _, out _));
            That(ex!.Message, Does.Contain("argCount"));
        }

        [Test]
        public void DescriptorTable_ExposesLoadTextKey_ForScriptAndTriggerGraph()
        {
            That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.LoadTextKey), Is.True);
            That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.LoadTextKey), Is.True);
            That(GraphOpDescriptorTable.GetLinearOutputType(GraphNodeOp.LoadTextKey), Is.EqualTo(GraphValueType.Text));
        }

        private static PresentationTextCatalog BuildCatalog(string tokenKey, string template, byte argCount = 0)
        {
            var tokenIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            int tokenId = tokenIds.Register(tokenKey);
            tokenIds.Freeze();
            var tokens = new PresentationTextTokenDefinition[tokenId + 1];
            tokens[tokenId] = new PresentationTextTokenDefinition
            {
                TokenId = tokenId,
                Key = tokenKey,
                ArgCount = argCount,
            };

            var localeIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            int localeId = localeIds.Register("zh-CN");
            localeIds.Freeze();

            var parts = new List<PresentationTextTemplatePart>();
            if (argCount == 0)
            {
                parts.Add(new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Literal, template, 0));
            }
            else
            {
                parts.Add(new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Argument, string.Empty, 0));
            }

            var localeTemplates = new PresentationTextTemplate[tokenId + 1];
            localeTemplates[tokenId] = new PresentationTextTemplate(template, parts.ToArray());
            var locales = new PresentationTextLocaleTable[localeId + 1];
            locales[localeId] = new PresentationTextLocaleTable(localeId, "zh-CN", localeTemplates);

            return new PresentationTextCatalog(tokenIds, tokens, localeIds, locales, defaultLocaleId: localeId);
        }

        private static void ExecuteScript(
            World world,
            GraphControlFlowDocument doc,
            IGraphRuntimeApi api,
            PresentationTextCatalog catalog,
            out int graphId,
            out GraphProgramRegistry programs)
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc, eventSchemas: null, enums: null);
            That(compiled.Diagnostics, Is.Empty, string.Join(Environment.NewLine, compiled.Diagnostics));
            That(compiled.Package, Is.Not.Null);

            var resolver = new TextTokenOnlyResolver(catalog);
            GraphProgramSymbolPatcher.Patch(
                compiled.Package!.Value.Symbols,
                compiled.Package.Value.Program,
                resolver);

            programs = new GraphProgramRegistry();
            graphId = 77;
            programs.Register(
                graphId,
                compiled.Package.Value.Program,
                GraphKind.Script,
                compiled.SourceMap,
                compiled.Package.Value.Symbols);

            var caster = world.Create();
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor();
            GraphSliceResult result = GraphExecutor.ExecuteScriptSlice(
                world,
                caster,
                Entity.Null,
                default,
                compiled.Package.Value.Program,
                api,
                programs,
                floats,
                ints,
                bools,
                entities,
                targets,
                callStack,
                ref cursor,
                budgetSteps: 256,
                graphId: graphId);
            That(result.Halted, Is.True, "text key script must halt");
        }

        private static GraphControlFlowDocument ScriptDoc(
            string id,
            GraphControlFlowNode[] nodes,
            GraphControlFlowEdge[] control,
            GraphControlFlowValueEdge[] values)
        {
            return new GraphControlFlowDocument
            {
                Id = id,
                Kind = "Script",
                Entry = nodes[0].Id,
                Nodes = new List<GraphControlFlowNode>(nodes),
                ControlEdges = new List<GraphControlFlowEdge>(control),
                ValueEdges = new List<GraphControlFlowValueEdge>(values),
            };
        }

        private static GraphControlFlowNode Node(
            string id,
            string op,
            string? textKey = null,
            string? surface = null)
        {
            return new GraphControlFlowNode
            {
                Id = id,
                Op = op,
                TextKey = textKey,
                PresentationSurface = surface,
            };
        }

        private static GraphControlFlowEdge Edge(string from, string port, string to)
            => new(from, port, to);

        private static GraphControlFlowValueEdge Value(string from, string fromPort, string to, string toPort)
            => new(from, fromPort, to, toPort);

        private sealed class TextTokenOnlyResolver : IGraphSymbolResolver
        {
            private readonly PresentationTextCatalog _catalog;

            public TextTokenOnlyResolver(PresentationTextCatalog catalog)
            {
                _catalog = catalog;
            }

            public int ResolveTextToken(string name)
            {
                int id = _catalog.GetTokenId(name);
                if (id <= 0)
                {
                    throw new InvalidOperationException($"unknown text token '{name}'");
                }

                return id;
            }

            public int ResolveTag(string name) => throw Unsupported(name);
            public int ResolveAttribute(string name) => throw Unsupported(name);
            public int ResolveEffectTemplate(string name) => throw Unsupported(name);
            public int ResolveRelationshipType(string name) => throw Unsupported(name);
            public int ResolveRelationshipMetric(string name) => throw Unsupported(name);
            public int ResolveRelationshipFlag(string name) => throw Unsupported(name);
            public int ResolveRelationshipReason(string name) => throw Unsupported(name);
            public int ResolveTargetDispatchPreset(string name) => throw Unsupported(name);
            public int ResolveEntityTemplate(string name) => throw Unsupported(name);

            private static Exception Unsupported(string name)
                => new InvalidOperationException($"unexpected symbol resolve '{name}'");
        }
    }
}
