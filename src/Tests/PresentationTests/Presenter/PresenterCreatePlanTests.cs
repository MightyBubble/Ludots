using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterCreatePlanTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PresenterCreatePlan", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            PresenterScopeTagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PresenterScopeTagRegistry.Clear();

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup races.
            }
        }

        [Test]
        public void ChildrenFieldParity_NestedTree_LocksScopeParamTransformAndOrder()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "parity_leaf" },
                  {
                    "id": "parity_mid",
                    "children": [
                      {
                        "definitionId": "parity_leaf",
                        "scopeTag": "leaf_scope",
                        "overrides": {
                          "params": [ { "paramKey": "parity.mid.scale", "lane": "Float", "floatValue": 3.5 } ],
                          "transform": {
                            "localPosition": [1, 2, 3],
                            "localRotation": [10, 20, 30],
                            "localScale": [2, 2, 2]
                          }
                        }
                      }
                    ]
                  },
                  {
                    "id": "parity_root",
                    "children": [
                      { "definitionId": "parity_mid", "scopeTag": "mid_scope" },
                      { "definitionId": "parity_leaf" }
                    ]
                  }
                ]
                """);
            var (definitions, world, runtime) = LoadAndBindDefinitions();

            int leafId = definitions.GetId("parity_leaf");
            int midId = definitions.GetId("parity_mid");
            int scaleKey = PresenterParamKeyRegistry.Register("parity.mid.scale");
            int leafScopeId = PresenterScopeTagRegistry.GetId("leaf_scope");
            int midScopeId = PresenterScopeTagRegistry.GetId("mid_scope");

            Entity owner = world.Create();
            Entity root = runtime.CreateHierarchy(
                definitions, definitions.GetId("parity_root"), owner, 77,
                PresentationAnchorKind.Entity, Vector3.Zero, 501, Entity.Null, null);

            Assert.That(runtime.ActiveCount, Is.EqualTo(4));

            PresenterChildren rootChildren = world.Get<PresenterChildren>(root);
            Assert.That(rootChildren.Count, Is.EqualTo(2));
            Entity mid = rootChildren.Get(0);
            Entity directLeaf = rootChildren.Get(1);
            Assert.That(world.Get<PresenterState>(mid).DefId, Is.EqualTo(midId));
            Assert.That(world.Get<PresenterState>(mid).ScopeId, Is.EqualTo(midScopeId));
            Assert.That(world.Get<PresenterState>(directLeaf).DefId, Is.EqualTo(leafId));
            Assert.That(world.Get<PresenterState>(directLeaf).ScopeId, Is.EqualTo(77));
            Assert.That(world.Get<PresenterParent>(mid).Parent, Is.EqualTo(root));
            Assert.That(world.Get<PresenterParent>(directLeaf).Parent, Is.EqualTo(root));

            PresenterChildren midChildren = world.Get<PresenterChildren>(mid);
            Assert.That(midChildren.Count, Is.EqualTo(1));
            Entity nestedLeaf = midChildren.Get(0);
            Assert.That(world.Get<PresenterState>(nestedLeaf).DefId, Is.EqualTo(leafId));
            Assert.That(world.Get<PresenterState>(nestedLeaf).ScopeId, Is.EqualTo(leafScopeId));
            Assert.That(world.Get<PresenterParent>(nestedLeaf).Parent, Is.EqualTo(mid));

            Assert.That(world.Has<PresenterInstanceTransformOverride>(nestedLeaf), Is.True);
            PresenterInstanceTransformOverride transformOverride =
                world.Get<PresenterInstanceTransformOverride>(nestedLeaf);
            Assert.That(transformOverride.LocalPosition, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(transformOverride.LocalScale, Is.EqualTo(new Vector3(2, 2, 2)));
            Quaternion expectedRotation = PresenterInstanceTransformOverride.RotationFromEulerDegreesXyz(new Vector3(10, 20, 30));
            Assert.That(transformOverride.LocalRotation.X, Is.EqualTo(expectedRotation.X).Within(0.0001f));
            Assert.That(transformOverride.LocalRotation.Y, Is.EqualTo(expectedRotation.Y).Within(0.0001f));
            Assert.That(transformOverride.LocalRotation.Z, Is.EqualTo(expectedRotation.Z).Within(0.0001f));
            Assert.That(transformOverride.LocalRotation.W, Is.EqualTo(expectedRotation.W).Within(0.0001f));
            Assert.That(world.Has<PresenterInstanceTransformOverride>(directLeaf), Is.False);

            Assert.That(runtime.ResolveFloat(nestedLeaf, scaleKey, -1f), Is.EqualTo(3.5f).Within(0.0001f));
            Assert.That(runtime.ResolveFloat(directLeaf, scaleKey, -1f), Is.EqualTo(-1f).Within(0.0001f));

            world.Dispose();
        }

        [Test]
        public void ChildrenFieldParity_InstanceChildrenEntries_ApplyOverridesAndNestedModes()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "parity_leaf" },
                  { "id": "parity_nested" },
                  { "id": "parity_shared", "children": [ { "definitionId": "parity_leaf" } ] },
                  {
                    "id": "parity_inst_root",
                    "children": [
                      {
                        "definitionId": "parity_shared",
                        "childrenMode": "Instance",
                        "instanceChildren": [
                          {
                            "definitionId": "parity_nested",
                            "scopeTag": "nested_scope",
                            "overrides": {
                              "params": [ { "paramKey": "parity.inst.flag", "lane": "Int", "intValue": 9 } ],
                              "transform": { "localPosition": [4, 5, 6] }
                            }
                          }
                        ]
                      }
                    ]
                  }
                ]
                """);
            var (definitions, world, runtime) = LoadAndBindDefinitions();

            int nestedId = definitions.GetId("parity_nested");
            int leafId = definitions.GetId("parity_leaf");
            int flagKey = PresenterParamKeyRegistry.Register("parity.inst.flag");
            int nestedScopeId = PresenterScopeTagRegistry.GetId("nested_scope");

            Entity owner = world.Create();
            Entity root = runtime.CreateHierarchy(
                definitions, definitions.GetId("parity_inst_root"), owner, 12,
                PresentationAnchorKind.Entity, Vector3.Zero, 502, Entity.Null, null);

            PresenterChildren rootChildren = world.Get<PresenterChildren>(root);
            Assert.That(rootChildren.Count, Is.EqualTo(1));
            Entity sharedChild = rootChildren.Get(0);
            Assert.That(world.Has<PresenterInstanceChildren>(sharedChild), Is.True);

            PresenterChildren instanceSubtree = world.Get<PresenterChildren>(sharedChild);
            Assert.That(instanceSubtree.Count, Is.EqualTo(1));
            Entity nested = instanceSubtree.Get(0);
            Assert.That(world.Get<PresenterState>(nested).DefId, Is.EqualTo(nestedId));
            Assert.That(world.Get<PresenterState>(nested).ScopeId, Is.EqualTo(nestedScopeId));
            Assert.That(world.Has<PresenterInstanceTransformOverride>(nested), Is.True);
            Assert.That(
                world.Get<PresenterInstanceTransformOverride>(nested).LocalPosition,
                Is.EqualTo(new Vector3(4, 5, 6)));
            Assert.That(runtime.ResolveInt(nested, flagKey, -1), Is.EqualTo(9));

            Assert.That(world.Get<PresenterChildren>(nested).Count, Is.EqualTo(0));
            Assert.That(definitions.Get(definitions.GetId("parity_shared")).Children[0].DefinitionId, Is.EqualTo(leafId));

            Entity plainSharedRoot = CreatePlainSharedRoot(definitions, world, runtime);
            PresenterChildren plainSubtree = world.Get<PresenterChildren>(plainSharedRoot);
            Assert.That(plainSubtree.Count, Is.EqualTo(1));
            Assert.That(world.Get<PresenterState>(plainSubtree.Get(0)).DefId, Is.EqualTo(leafId));
            Assert.That(world.Has<PresenterInstanceChildren>(plainSubtree.Get(0)), Is.False);

            world.Dispose();
        }

        [Test]
        public void Compile_NestedDeclarationOrder_ProducesParentBeforeChildNodesWithEdges()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "plan_leaf" },
                  { "id": "plan_mid", "children": [ { "definitionId": "plan_leaf", "scopeTag": "leaf_scope" } ] },
                  {
                    "id": "plan_root",
                    "children": [
                      { "definitionId": "plan_mid", "scopeTag": "mid_scope" },
                      { "definitionId": "plan_leaf" }
                    ]
                  }
                ]
                """);
            var (definitions, world, runtime) = LoadAndBindDefinitions();

            PresenterCreatePlan plan = definitions.GetOrCreateCreatePlan(definitions.GetId("plan_root"));

            Assert.That(plan.RootDefinitionId, Is.EqualTo(definitions.GetId("plan_root")));
            Assert.That(plan.Nodes.Length, Is.EqualTo(3));
            Assert.That(plan.Nodes[0].DefinitionId, Is.EqualTo(definitions.GetId("plan_mid")));
            Assert.That(plan.Nodes[0].ParentNodeIndex, Is.EqualTo(-1));
            Assert.That(plan.Nodes[0].ScopeTag, Is.EqualTo(PresenterScopeTagRegistry.GetId("mid_scope")));
            Assert.That(plan.Nodes[0].Path, Is.EqualTo("root/children[0]"));
            Assert.That(plan.Nodes[1].DefinitionId, Is.EqualTo(definitions.GetId("plan_leaf")));
            Assert.That(plan.Nodes[1].ParentNodeIndex, Is.EqualTo(0));
            Assert.That(plan.Nodes[1].ScopeTag, Is.EqualTo(PresenterScopeTagRegistry.GetId("leaf_scope")));
            Assert.That(plan.Nodes[1].Path, Is.EqualTo("root/children[0]/children[0]"));
            Assert.That(plan.Nodes[2].DefinitionId, Is.EqualTo(definitions.GetId("plan_leaf")));
            Assert.That(plan.Nodes[2].ParentNodeIndex, Is.EqualTo(-1));
            Assert.That(plan.Nodes[2].ScopeTag, Is.EqualTo(-1));
            Assert.That(plan.Nodes[2].Path, Is.EqualTo("root/children[1]"));
            for (int i = 0; i < plan.Nodes.Length; i++)
            {
                Assert.That(plan.Nodes[i].ParentNodeIndex, Is.LessThan(i), "plan must order parent before child");
            }

            world.Dispose();
        }

        [Test]
        public void Compile_InstanceChildren_MaterializesInstanceSubtreeNotDefinitionChildren()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "plan_shared_leaf" },
                  { "id": "plan_inst_leaf" },
                  { "id": "plan_shared", "children": [ { "definitionId": "plan_shared_leaf" } ] },
                  {
                    "id": "plan_inst_root",
                    "children": [
                      {
                        "definitionId": "plan_shared",
                        "childrenMode": "Instance",
                        "instanceChildren": [ { "definitionId": "plan_inst_leaf" } ]
                      }
                    ]
                  }
                ]
                """);
            var (definitions, world, runtime) = LoadAndBindDefinitions();

            PresenterCreatePlan plan = definitions.GetOrCreateCreatePlan(definitions.GetId("plan_inst_root"));

            Assert.That(plan.Nodes.Length, Is.EqualTo(2));
            Assert.That(plan.Nodes[0].DefinitionId, Is.EqualTo(definitions.GetId("plan_shared")));
            Assert.That(plan.Nodes[0].ParentNodeIndex, Is.EqualTo(-1));
            Assert.That(plan.Nodes[0].Path, Is.EqualTo("root/children[0]"));
            Assert.That(plan.Nodes[1].DefinitionId, Is.EqualTo(definitions.GetId("plan_inst_leaf")));
            Assert.That(plan.Nodes[1].ParentNodeIndex, Is.EqualTo(0));
            Assert.That(plan.Nodes[1].Path, Is.EqualTo("root/children[0]/instanceChildren[0]"));

            PresenterCreatePlan plainPlan = definitions.GetOrCreateCreatePlan(definitions.GetId("plan_shared"));
            Assert.That(plainPlan.Nodes.Length, Is.EqualTo(1));
            Assert.That(plainPlan.Nodes[0].DefinitionId, Is.EqualTo(definitions.GetId("plan_shared_leaf")));

            world.Dispose();
        }

        [Test]
        public void Compile_EmptyInstanceChildren_PlanStopsAtOverrideOwner()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "plan_leaf" },
                  { "id": "plan_shared", "children": [ { "definitionId": "plan_leaf" } ] },
                  {
                    "id": "plan_empty_root",
                    "children": [
                      { "definitionId": "plan_shared", "childrenMode": "Instance", "instanceChildren": [] }
                    ]
                  }
                ]
                """);
            var (definitions, world, runtime) = LoadAndBindDefinitions();

            PresenterCreatePlan plan = definitions.GetOrCreateCreatePlan(definitions.GetId("plan_empty_root"));

            Assert.That(plan.Nodes.Length, Is.EqualTo(1));
            Assert.That(plan.Nodes[0].DefinitionId, Is.EqualTo(definitions.GetId("plan_shared")));

            world.Dispose();
        }

        [Test]
        public void Compile_CycleThroughInstanceChildren_IsRejectedAtLoad()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "cycle_b" },
                  {
                    "id": "cycle_root",
                    "children": [
                      {
                        "definitionId": "cycle_b",
                        "childrenMode": "Instance",
                        "instanceChildren": [ { "definitionId": "cycle_root" } ]
                      }
                    ]
                  }
                ]
                """);
            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new PresenterDefinitionConfigLoader(pipeline, registry).Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("Circular child reference detected"));
            Assert.That(ex.Message, Does.Contain("cycle_root"));
        }

        [Test]
        public void Compile_ParamOverrideWithInvalidType_ThrowsWithChildPath()
        {
            using var world = Arch.Core.World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int childId = definitions.Register("plan.bad-lane-child", new PresenterDefinition());
            int rootId = definitions.Register("plan.bad-lane-root", new PresenterDefinition
            {
                Children =
                [
                    new ChildPresenterRef
                    {
                        DefinitionId = childId,
                        ParamOverrides = new[] { new ParamDefault { ParamKey = -1, Lane = ParamLane.Float } },
                    },
                ],
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => definitions.GetOrCreateCreatePlan(rootId))!;
            Assert.That(ex.Message, Does.Contain(PresenterCreatePlanCompiler.ParamOverrideTypeError));
            Assert.That(ex.Message, Does.Contain("childPath='root/children[0]'"));

            world.Dispose();
        }

        [Test]
        public void Compile_InstanceChildrenOverCapacity_ThrowsWithChildSource()
        {
            using var world = Arch.Core.World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int leafId = definitions.Register("plan.capacity-leaf", new PresenterDefinition());
            var instanceChildren = new ChildPresenterRef[PresenterChildren.MAX_CHILDREN + 1];
            for (int i = 0; i < instanceChildren.Length; i++)
            {
                instanceChildren[i] = new ChildPresenterRef { DefinitionId = leafId };
            }

            int childId = definitions.Register("plan.capacity-child", new PresenterDefinition());
            int rootId = definitions.Register("plan.capacity-root", new PresenterDefinition
            {
                Children =
                [
                    new ChildPresenterRef
                    {
                        DefinitionId = childId,
                        InstanceOverride = new PresenterChildInstanceOverride
                        {
                            ChildrenMode = PresenterChildrenMode.Instance,
                            InstanceChildren = instanceChildren,
                        },
                    },
                ],
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => definitions.GetOrCreateCreatePlan(rootId))!;
            Assert.That(ex.Message, Does.Contain(PresenterCreatePlanCompiler.ChildCapacityError));
            Assert.That(ex.Message, Does.Contain("instanceChildren"));
            Assert.That(ex.Message, Does.Contain($"capacity={PresenterChildren.MAX_CHILDREN}"));

            world.Dispose();
        }

        [Test]
        public void Registry_ReRegisteredChildDefinition_InvalidatesCachedPlan()
        {
            using var world = Arch.Core.World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int leafId = definitions.Register("plan.leaf", new PresenterDefinition());
            int otherLeafId = definitions.Register("plan.other-leaf", new PresenterDefinition());
            int rootId = definitions.Register("plan.recompile-root", new PresenterDefinition
            {
                Children = [new ChildPresenterRef { DefinitionId = leafId }],
            });

            PresenterCreatePlan first = definitions.GetOrCreateCreatePlan(rootId);
            Assert.That(first.Nodes.Length, Is.EqualTo(1));

            _ = definitions.Register("plan.leaf", new PresenterDefinition
            {
                Children = [new ChildPresenterRef { DefinitionId = otherLeafId }],
            });
            PresenterCreatePlan second = definitions.GetOrCreateCreatePlan(rootId);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Nodes.Length, Is.EqualTo(2));
            Assert.That(second.Nodes[1].DefinitionId, Is.EqualTo(otherLeafId));
            Assert.That(second.Nodes[1].ParentNodeIndex, Is.EqualTo(0));

            world.Dispose();
        }

        [Test]
        public void CreateHierarchy_WritesSearchableTracePerPlanNode()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "trace_leaf" },
                  { "id": "trace_mid", "children": [ { "definitionId": "trace_leaf", "scopeTag": "leaf_scope" } ] },
                  {
                    "id": "trace_root",
                    "children": [
                      { "definitionId": "trace_mid", "scopeTag": "mid_scope" },
                      { "definitionId": "trace_leaf" }
                    ]
                  }
                ]
                """);
            var (definitions, world, runtime) = LoadAndBindDefinitions();

            Entity owner = world.Create();
            int nextStableId = 700;
            Entity root = runtime.CreateHierarchy(
                definitions, definitions.GetId("trace_root"), owner, 21,
                PresentationAnchorKind.Entity, Vector3.Zero, nextStableId++, Entity.Null, null,
                allocateStableId: () => nextStableId++);
            int rootStableId = world.Get<PresenterState>(root).StableId;

            Assert.That(runtime.CreateTraceCount, Is.EqualTo(4));
            PresenterCreateTraceEntry[] traces = runtime.FindCreateTraces(rootStableId);
            Assert.That(traces.Length, Is.EqualTo(4));

            Assert.That(traces[0].NodeIndex, Is.EqualTo(-1));
            Assert.That(traces[0].Path, Is.EqualTo("root"));
            Assert.That(traces[0].Created, Is.EqualTo(root));
            Assert.That(traces[0].Parent, Is.EqualTo(Entity.Null));
            Assert.That(traces[0].DefinitionId, Is.EqualTo(definitions.GetId("trace_root")));
            Assert.That(traces[0].ScopeId, Is.EqualTo(21));

            Assert.That(traces[1].NodeIndex, Is.EqualTo(0));
            Assert.That(traces[1].Path, Is.EqualTo("root/children[0]"));
            Assert.That(traces[1].Parent, Is.EqualTo(root));
            Assert.That(traces[1].DefinitionId, Is.EqualTo(definitions.GetId("trace_mid")));
            Assert.That(traces[1].ScopeId, Is.EqualTo(PresenterScopeTagRegistry.GetId("mid_scope")));
            Assert.That(traces[1].Created, Is.EqualTo(world.Get<PresenterChildren>(root).Get(0)));

            Assert.That(traces[2].NodeIndex, Is.EqualTo(1));
            Assert.That(traces[2].Path, Is.EqualTo("root/children[0]/children[0]"));
            Assert.That(traces[2].Parent, Is.EqualTo(traces[1].Created));
            Assert.That(traces[2].DefinitionId, Is.EqualTo(definitions.GetId("trace_leaf")));
            Assert.That(traces[2].ScopeId, Is.EqualTo(PresenterScopeTagRegistry.GetId("leaf_scope")));

            Assert.That(traces[3].NodeIndex, Is.EqualTo(2));
            Assert.That(traces[3].Path, Is.EqualTo("root/children[1]"));
            Assert.That(traces[3].Parent, Is.EqualTo(root));
            Assert.That(traces[3].ScopeId, Is.EqualTo(21));

            Assert.That(runtime.FindCreateTraces(999999), Is.Empty);

            world.Dispose();
        }

        [Test]
        public void CreateHierarchy_ParentMissingMidPlan_ThrowsStructuredErrorAndLeavesNoResidue()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "residue_leaf" },
                  { "id": "residue_mid", "children": [ { "definitionId": "residue_leaf" } ] },
                  { "id": "residue_root", "children": [ { "definitionId": "residue_mid" } ] }
                ]
                """);
            var (definitions, world, runtime) = LoadAndBindDefinitions();
            int midId = definitions.GetId("residue_mid");

            Entity owner = world.Create();
            int allocations = 0;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                runtime.CreateHierarchy(
                    definitions, definitions.GetId("residue_root"), owner, 30,
                    PresentationAnchorKind.Entity, Vector3.Zero, 800, Entity.Null, null,
                    allocateStableId: () =>
                    {
                        allocations++;
                        if (allocations == 2)
                        {
                            DestroyAllMidEntities(world, runtime, midId);
                        }

                        return 800 + allocations;
                    }))!;

            Assert.That(ex.Message, Does.Contain(PresenterCreatePlanCompiler.PlanParentMissingError));
            Assert.That(ex.Message, Does.Contain("childPath='root/children[0]/children[0]'"));
            Assert.That(ex.Message, Does.Contain("planNode=1"));
            Assert.That(runtime.ActiveCount, Is.EqualTo(0));

            int survivors = 0;
            world.Query(new QueryDescription().WithAll<PresenterState>(), (Entity _, ref PresenterState __) => survivors++);
            Assert.That(survivors, Is.EqualTo(0));

            world.Dispose();
        }

        [Test]
        public void CreateHierarchy_InactiveRootParent_ThrowsStructuredErrorWithoutResidue()
        {
            WriteCatalog();
            WritePresenters("[ { \"id\": \"parent_root\" }, { \"id\": \"parent_child\" } ]");
            var (definitions, world, runtime) = LoadAndBindDefinitions();

            Entity owner = world.Create();
            Entity deadParent = world.Create();
            world.Destroy(deadParent);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                runtime.CreateHierarchy(
                    definitions, definitions.GetId("parent_child"), owner, 31,
                    PresentationAnchorKind.Entity, Vector3.Zero, 801, deadParent, null))!;
            Assert.That(ex.Message, Does.Contain(PresenterCreatePlanCompiler.PlanParentMissingError));
            Assert.That(ex.Message, Does.Contain("childPath='root'"));
            Assert.That(ex.Message, Does.Contain("planNode=-1"));
            Assert.That(runtime.ActiveCount, Is.EqualTo(0));

            world.Dispose();
        }

        [Test]
        public void CreateHierarchy_RootAttachCapacityFailure_LeavesNoChildBehind()
        {
            using var world = Arch.Core.World.Create();
            var definitions = new PresenterDefinitionRegistry();
            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            int leafId = definitions.Register("plan.capacity-leaf", new PresenterDefinition());
            int treeId = definitions.Register("plan.capacity-tree", new PresenterDefinition
            {
                Children = [new ChildPresenterRef { DefinitionId = leafId }],
            });
            int rootId = definitions.Register("plan.capacity-root", new PresenterDefinition());

            Entity owner = world.Create();
            Entity root = runtime.CreateHierarchy(
                definitions, rootId, owner, 40, PresentationAnchorKind.Entity, Vector3.Zero, 810, Entity.Null, null);
            for (int i = 0; i < PresenterChildren.MAX_CHILDREN; i++)
            {
                runtime.Create(leafId, owner, 90 + i, PresentationAnchorKind.Entity, Vector3.Zero, 811 + i, root, null);
            }

            int activeBefore = runtime.ActiveCount;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                runtime.CreateHierarchy(
                    definitions, treeId, owner, 41, PresentationAnchorKind.Entity, Vector3.Zero, 850, root, null))!;
            Assert.That(ex.Message, Does.Contain("exceeded child capacity"));
            Assert.That(runtime.ActiveCount, Is.EqualTo(activeBefore));
            Assert.That(world.Get<PresenterChildren>(root).Count, Is.EqualTo(PresenterChildren.MAX_CHILDREN));

            world.Dispose();
        }

        [Test]
        public void CreateEntityAnchoredRootBatch_WritesTraceForRootsAndPlanChildren()
        {
            using var world = Arch.Core.World.Create();
            var definitions = new PresenterDefinitionRegistry();
            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            int childId = definitions.Register("plan.batch-child", new PresenterDefinition());
            int rootId = definitions.Register("plan.batch-root", new PresenterDefinition
            {
                Children = [new ChildPresenterRef { DefinitionId = childId }],
            });

            Entity ownerA = world.Create(new VisualTransform(), new CullState());
            Entity ownerB = world.Create(new VisualTransform(), new CullState());
            Span<Entity> created = stackalloc Entity[2];
            int count = runtime.CreateEntityAnchoredRootBatch(
                definitions,
                rootId,
                new[] { ownerA, ownerB },
                new[] { 51, 52 },
                new[] { 860, 861 },
                new[]
                {
                    world.Get<VisualTransform>(ownerA),
                    world.Get<VisualTransform>(ownerB),
                },
                new[]
                {
                    world.Get<CullState>(ownerA),
                    world.Get<CullState>(ownerB),
                },
                definitions.Get(rootId),
                created,
                allocateStableId: () => 862);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(runtime.CreateTraceCount, Is.EqualTo(4));

            PresenterCreateTraceEntry[] tracesA = runtime.FindCreateTraces(860);
            Assert.That(tracesA.Length, Is.EqualTo(2));
            Assert.That(tracesA[0].NodeIndex, Is.EqualTo(-1));
            Assert.That(tracesA[0].Created, Is.EqualTo(created[0]));
            Assert.That(tracesA[1].NodeIndex, Is.EqualTo(0));
            Assert.That(tracesA[1].Path, Is.EqualTo("root/children[0]"));
            Assert.That(tracesA[1].Parent, Is.EqualTo(created[0]));
            Assert.That(tracesA[1].Created, Is.EqualTo(world.Get<PresenterChildren>(created[0]).Get(0)));
            Assert.That(tracesA[1].DefinitionId, Is.EqualTo(childId));

            PresenterCreateTraceEntry[] tracesB = runtime.FindCreateTraces(861);
            Assert.That(tracesB.Length, Is.EqualTo(2));
            Assert.That(tracesB[0].Created, Is.EqualTo(created[1]));
            Assert.That(tracesB[1].Parent, Is.EqualTo(created[1]));

            world.Dispose();
        }

        private static void DestroyAllMidEntities(World world, PresenterEntityRuntime runtime, int midId)
        {
            var mids = new List<Entity>();
            world.Query(new QueryDescription().WithAll<PresenterState>(), (Entity entity, ref PresenterState state) =>
            {
                if (state.DefId == midId)
                {
                    mids.Add(entity);
                }
            });

            for (int i = 0; i < mids.Count; i++)
            {
                runtime.Destroy(mids[i]);
            }
        }

        private Entity CreatePlainSharedRoot(
            PresenterDefinitionRegistry definitions, World world, PresenterEntityRuntime runtime)
        {
            Entity owner = world.Create();
            return runtime.CreateHierarchy(
                definitions, definitions.GetId("parity_shared"), owner, 13,
                PresentationAnchorKind.Entity, Vector3.Zero, 503, Entity.Null, null);
        }

        private (PresenterDefinitionRegistry Definitions, World World, PresenterEntityRuntime Runtime) LoadAndBindDefinitions()
        {
            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            new PresenterDefinitionConfigLoader(pipeline, registry).Load(catalog);
            var world = Arch.Core.World.Create();
            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(registry);
            return (registry, world, runtime);
        }

        private (VirtualFileSystem Vfs, ModLoader ModLoader, ConfigPipeline Pipeline, ConfigCatalog Catalog) BuildPipeline()
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            return (vfs, modLoader, pipeline, catalog);
        }

        private void WriteCatalog()
        {
            WriteFile(
                "Core",
                "config_catalog.json",
                @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
        }

        private void WritePresenters(string content)
        {
            WriteFile("Core", "Presentation/presenters.json", content);
        }

        private void WriteFile(string modId, string relativePath, string content)
        {
            string dir = Path.Combine(_root, modId, Path.GetDirectoryName(relativePath) ?? string.Empty);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }
    }
}
