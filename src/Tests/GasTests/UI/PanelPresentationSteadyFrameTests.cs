using System;
using System.Linq;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI;
using Ludots.UI.Panels;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    /// <summary>
    /// 面板呈现每帧都跑：没有实例时零分配；挂着的行式面板读数、位置、皮肤都没变时不重建表面、不分配，
    /// 读数变了才重建一次。
    /// </summary>
    [TestFixture]
    public sealed class PanelPresentationSteadyFrameTests
    {
        private const string TemplateId = "tests.panel.steady";
        private const string AnchorId = "screen.top_left";
        private const string TemplateJson = """
        {
          "id": "tests.panel.steady",
          "graph": "tests.graph.panel.steady",
          "pins": [
            { "name": "hp", "key": "tests.panel.steady.hp", "mode": "realtime", "default": 0 }
          ]
        }
        """;

        private World _world = null!;
        private Entity _scope;
        private GraphOutputValueStore _store = null!;
        private PanelHost _host = null!;
        private UiPanelActivationStore _activation = null!;
        private UIRoot _root = null!;
        private UiSurfaceHost _surface = null!;
        private PanelPresentationSystem _system = null!;

        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            ConfigKeyRegistry.Clear();
            GraphIdRegistry.Clear();
            _world = World.Create();
            _scope = _world.Create();
            _world.Add(_scope, new AttributeBuffer());

            var templates = new PanelTemplateRegistry();
            templates.Register(PanelTemplateLoader.Load(TemplateJson));
            templates.Freeze();
            foreach (PanelTemplate template in templates.Snapshot())
            {
                template.GraphId = -1;
            }

            var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _store = new GraphOutputValueStore(keys, initialCapacity: 8);
            _store.SetFloat(_scope, "tests.panel.steady.hp", 30f);
            _host = new PanelHost(templates, new PanelProjectionReader(_world, _store));
            _activation = new UiPanelActivationStore();
            new PanelActivationApi(_activation).ShowPanel(TemplateId);

            _root = new UIRoot(new NullUiRenderer());
            _root.Resize(1280f, 720f);
            var textMeasurer = new SkiaTextMeasurer();
            var imageSizeProvider = new SkiaImageSizeProvider();
            _surface = new UiSurfaceHost(_root, textMeasurer, imageSizeProvider);
            _system = new PanelPresentationSystem(
                _host,
                templates,
                _activation,
                _surface,
                _root,
                globalSkin: null,
                textMeasurer: textMeasurer,
                imageSizeProvider: imageSizeProvider);
        }

        [TearDown]
        public void TearDown()
        {
            _system.Dispose();
            _world.Dispose();
            AttributeRegistry.Clear();
            ConfigKeyRegistry.Clear();
            GraphIdRegistry.Clear();
        }

        [Test]
        public void NoInstances_FrameAllocatesNothing()
        {
            for (int i = 0; i < 8; i++) Frame();

            long allocated = Measure(32);

            Assert.That(allocated, Is.EqualTo(0L), $"没有面板实例时每帧分配了 {allocated} 字节");
        }

        [Test]
        public void MountedRowPanel_UnchangedValues_NoRebuildNoAllocation_ThenRebuildsOnChange()
        {
            _host.Instantiate(TemplateId, AnchorId, _scope);
            for (int i = 0; i < 8; i++) Frame();
            UiScene scene = _surface.Scene ?? throw new AssertionException("面板没挂上");
            Assert.That(RowText(scene), Does.Contain("30"));
            long version = scene.Version;

            long allocated = Measure(32);

            Assert.That(allocated, Is.EqualTo(0L), $"读数不变的面板帧分配了 {allocated} 字节");
            Assert.That(scene.Version, Is.EqualTo(version), "读数不变不该重建表面");

            _store.SetFloat(_scope, "tests.panel.steady.hp", 45f);
            Frame();
            Assert.That(scene.Version, Is.GreaterThan(version), "读数变了要重建一次");
            Assert.That(RowText(scene), Does.Contain("45"));
        }

        private void Frame()
        {
            _host.RefreshRealtime();
            _system.Update(0.016f);
            if (_root.IsDirty) _root.Render();
        }

        private long Measure(int frames)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < frames; i++) Frame();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static string RowText(UiScene scene)
        {
            UiNode? row = Find(scene.Root, "row-hp");
            return row?.TextContent ?? throw new AssertionException("面板里没有 hp 行");
        }

        private static UiNode? Find(UiNode? node, string className)
        {
            if (node == null) return null;
            if (node.ClassNames.Contains(className)) return node;
            for (int i = 0; i < node.Children.Count; i++)
            {
                UiNode? found = Find(node.Children[i], className);
                if (found != null) return found;
            }

            return null;
        }

        private sealed class NullUiRenderer : IUiRenderer
        {
            public void Render(UiScene scene, float width, float height)
            {
            }
        }
    }
}
