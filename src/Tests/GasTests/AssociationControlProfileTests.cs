using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Modding;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 CTRL-4b: generic predicate → Controls-edge rule engine (§5.4, M3 scenarios).
    /// The trigger tag string used here is pure test data; Core never interprets it.
    /// </summary>
    [TestFixture]
    public sealed class AssociationControlProfileTests
    {
        private const string TriggerTag = "participant.offline";

        [SetUp]
        public void SetUp() => TagRegistry.Clear();

        [TearDown]
        public void TearDown() => TagRegistry.Clear();

        [Test]
        public void TriggerTag_GrantsControlsEdgeWithGrantedFlag_AndRevokeRemovesIt()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, CreateProxyProfileCatalog());

            harness.Runtime.Update();
            Assert.That(harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId), Is.False);

            harness.AddTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();

            Assert.That(harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId), Is.True);
            Assert.That(harness.Relationships.HasFlag(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId, harness.GrantedFlagId), Is.True);
            Assert.That(
                harness.Relationships.HasLink(harness.P2Rep, harness.P1Rep, harness.ControlsTypeId),
                Is.False,
                "The grantor is offline, not the grantee; the edge is directional.");

            harness.RemoveTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();

            Assert.That(harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId), Is.False);
        }

        [Test]
        public void ManualControlsEdge_IsNeverRevokedByTheProfile()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, CreateProxyProfileCatalog());
            harness.Relationships.EnsureLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId);

            harness.AddTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();
            Assert.That(
                harness.Relationships.HasFlag(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId, harness.GrantedFlagId),
                Is.False,
                "A pre-existing manual edge must not be claimed by the profile.");

            harness.RemoveTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();

            Assert.That(
                harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId),
                Is.True,
                "Revoke only removes edges the profile granted.");
        }

        [Test]
        public void WithoutAllyRelationship_TriggerTagAloneGrantsNothing()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, CreateProxyProfileCatalog(), linkAllies: false);

            harness.AddTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();

            Assert.That(harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId), Is.False);
        }

        [Test]
        public void AnyAndNotCombinators_EvaluateCorrectly()
        {
            const string tagA = "test.trigger.a";
            const string tagB = "test.trigger.b";
            const string tagVeto = "test.veto";
            var catalog = new AssociationControlProfileCatalogConfig
            {
                Profiles =
                {
                    new AssociationControlProfileConfig
                    {
                        Id = "profile.test.any_not",
                        When = new AssociationControlConditionConfig
                        {
                            All = new List<AssociationControlConditionConfig>
                            {
                                new()
                                {
                                    Any = new List<AssociationControlConditionConfig>
                                    {
                                        new() { Tag = tagA, On = "grantor" },
                                        new() { Tag = tagB, On = "grantor" },
                                    },
                                },
                                new() { Not = new AssociationControlConditionConfig { Tag = tagVeto, On = "grantee" } },
                            },
                        },
                        Grant = new AssociationControlGrantConfig { EdgeType = "Controls", From = "grantee", To = "grantor" },
                        RevokeWhen = new AssociationControlConditionConfig
                        {
                            Not = new AssociationControlConditionConfig
                            {
                                Any = new List<AssociationControlConditionConfig>
                                {
                                    new() { Tag = tagA, On = "grantor" },
                                    new() { Tag = tagB, On = "grantor" },
                                },
                            },
                        },
                    },
                },
            };

            using var world = World.Create();
            Harness harness = Harness.Create(world, catalog);

            harness.Runtime.Update();
            Assert.That(harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId), Is.False);

            harness.AddTag(harness.P2Rep, tagB);
            harness.AddTag(harness.P1Rep, tagVeto);
            harness.Runtime.Update();
            Assert.That(
                harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId),
                Is.False,
                "not(veto on grantee) must block the grant.");

            harness.RemoveTag(harness.P1Rep, tagVeto);
            harness.Runtime.Update();
            Assert.That(harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId), Is.True, "any(a|b) with b present must grant.");

            harness.RemoveTag(harness.P2Rep, tagB);
            harness.Runtime.Update();
            Assert.That(harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId), Is.False, "not(any(a|b)) must revoke.");
        }

        [Test]
        public void UnchangedTicks_SkipEvaluationAndAllocateZero()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, CreateProxyProfileCatalog());
            harness.AddTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();
            int passesAfterGrant = harness.Runtime.EvaluationPassCount;
            harness.Runtime.Update();

            Assert.That(harness.Runtime.EvaluationPassCount, Is.EqualTo(passesAfterGrant), "A no-change tick must not evaluate profiles.");

            long allocated = MeasureUnchangedTickAllocations(harness);
            allocated = Math.Min(allocated, MeasureUnchangedTickAllocations(harness));
            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(harness.Runtime.EvaluationPassCount, Is.EqualTo(passesAfterGrant));

            harness.RemoveTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();
            Assert.That(harness.Runtime.EvaluationPassCount, Is.EqualTo(passesAfterGrant + 1), "A tag change must trigger exactly one evaluation pass.");
        }

        [Test]
        public void IrrelevantTagChanges_DoNotTriggerEvaluation()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, CreateProxyProfileCatalog());
            harness.AddTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();
            int passesAfterGrant = harness.Runtime.EvaluationPassCount;

            for (int i = 0; i < 16; i++)
            {
                harness.AddTag(harness.P1Rep, "test.unrelated");
                harness.Runtime.Update();
                harness.RemoveTag(harness.P1Rep, "test.unrelated");
                harness.Runtime.Update();
            }

            Assert.That(
                harness.Runtime.EvaluationPassCount,
                Is.EqualTo(passesAfterGrant),
                "Tags no profile predicate references must never trigger an evaluation pass.");

            harness.RemoveTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();
            Assert.That(harness.Runtime.EvaluationPassCount, Is.EqualTo(passesAfterGrant + 1), "A referenced tag change still evaluates.");
            Assert.That(harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId), Is.False);
        }

        [Test]
        public void RuleDisableMaskTagChange_TriggersReevaluation()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, CreateProxyProfileCatalog());
            int triggerTagId = TagRegistry.GetId(TriggerTag);
            int suppressorTagId = TagRegistry.Register("test.suppressor");
            var ruleSet = new TagRuleSet();
            unsafe
            {
                ruleSet.DisabledIfTags[0] = suppressorTagId;
                ruleSet.DisabledIfCount = 1;
            }

            harness.TagOps.RegisterTagRuleSet(triggerTagId, ruleSet);

            harness.AddTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();
            Assert.That(harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId), Is.True);

            // The suppressor is not referenced by any predicate directly, but it flips the trigger
            // tag's effective sense; the relevance mask must include the rule's disable bits.
            harness.AddTag(harness.P2Rep, "test.suppressor");
            harness.Runtime.Update();
            Assert.That(
                harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId),
                Is.False,
                "Disabling the trigger tag via a rule must revoke the granted edge.");
        }

        [Test]
        public void Loader_DuplicateIdWithinFragment_FailsFast()
        {
            string root = Path.Combine(Path.GetTempPath(), $"ControlProfileTest_{Guid.NewGuid():N}");
            try
            {
                string relationshipsDir = Path.Combine(root, "Configs", "Relationships");
                Directory.CreateDirectory(relationshipsDir);
                File.WriteAllText(Path.Combine(relationshipsDir, "control_profiles.json"), """
                {
                  "profiles": [
                    { "id": "profile.control.dup", "when": { "tag": "test.a", "on": "grantor" }, "grant": { "edgeType": "Controls", "from": "grantee", "to": "grantor" } },
                    { "id": "profile.control.dup", "when": { "tag": "test.b", "on": "grantor" }, "grant": { "edgeType": "Controls", "from": "grantee", "to": "grantor" } }
                  ]
                }
                """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));

                var exception = Assert.Throws<InvalidOperationException>(
                    () => new AssociationControlProfilePipelineLoader(pipeline).Load());
                Assert.That(exception!.Message, Does.Contain("duplicate profile id 'profile.control.dup'"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void Loader_SameIdAcrossFragments_LaterFragmentWins_AndConflictReportRecordsWinner()
        {
            string root = Path.Combine(Path.GetTempPath(), $"ControlProfileTest_{Guid.NewGuid():N}");
            try
            {
                // Fragment 1: Core:Configs/... (engine defaults). Fragment 2: Core:... (loaded after,
                // same position as a mod override in LoadFromAllSources order).
                string defaultsDir = Path.Combine(root, "Configs", "Relationships");
                string overrideDir = Path.Combine(root, "Relationships");
                Directory.CreateDirectory(defaultsDir);
                Directory.CreateDirectory(overrideDir);
                File.WriteAllText(Path.Combine(defaultsDir, "control_profiles.json"), """
                {
                  "profiles": [
                    { "id": "profile.control.shared", "when": { "tag": "test.default", "on": "grantor" }, "grant": { "edgeType": "Controls", "from": "grantee", "to": "grantor" } }
                  ]
                }
                """);
                File.WriteAllText(Path.Combine(overrideDir, "control_profiles.json"), """
                {
                  "profiles": [
                    { "id": "profile.control.shared", "when": { "tag": "test.override", "on": "grantor" }, "grant": { "edgeType": "Controls", "from": "grantee", "to": "grantor" } }
                  ]
                }
                """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
                var report = new ConfigConflictReport();

                AssociationControlProfileCatalogConfig catalog =
                    new AssociationControlProfilePipelineLoader(pipeline).Load(report: report);

                Assert.That(catalog.Profiles, Has.Count.EqualTo(1));
                Assert.That(catalog.Profiles[0].When!.Tag, Is.EqualTo("test.override"), "The later fragment must win.");
                Assert.That(
                    report.TryGetWinner("Relationships/control_profiles.json", "profile.control.shared", out string winner),
                    Is.True,
                    "The cross-fragment override must be recorded in the conflict report.");
                Assert.That(winner, Is.EqualTo("Core:Relationships/control_profiles.json"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void Loader_ReadsProfilesFromJson_AndCoreCarriesNoScenarioLiterals()
        {
            string root = Path.Combine(Path.GetTempPath(), $"ControlProfileTest_{Guid.NewGuid():N}");
            try
            {
                string relationshipsDir = Path.Combine(root, "Configs", "Relationships");
                Directory.CreateDirectory(relationshipsDir);
                File.WriteAllText(Path.Combine(relationshipsDir, "control_profiles.json"), """
                {
                  "profiles": [
                    {
                      "id": "profile.control.ally_offline_proxy",
                      "when": {
                        "all": [
                          { "relationship": "Ally", "between": ["grantee", "grantor"] },
                          { "tag": "participant.offline", "on": "grantor" }
                        ]
                      },
                      "grant": { "edgeType": "Controls", "from": "grantee", "to": "grantor" },
                      "revokeWhen": { "not": { "tag": "participant.offline", "on": "grantor" } }
                    }
                  ]
                }
                """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));

                AssociationControlProfileCatalogConfig catalog = new AssociationControlProfilePipelineLoader(pipeline).Load();

                Assert.That(catalog.Profiles, Has.Count.EqualTo(1));
                Assert.That(catalog.Profiles[0].Id, Is.EqualTo("profile.control.ally_offline_proxy"));

                using var world = World.Create();
                Harness harness = Harness.Create(world, catalog);
                harness.AddTag(harness.P2Rep, "participant.offline");
                harness.Runtime.Update();
                Assert.That(harness.Relationships.HasLink(harness.P1Rep, harness.P2Rep, harness.ControlsTypeId), Is.True);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }

            AssertCoreAssociationSourcesCarryNoScenarioLiterals();
        }

        [Test]
        public void EndToEnd_ProfileDrivenControlsEdge_RoutesWritesAndComposesView()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, CreateProxyProfileCatalog());
            Entity m01 = world.Create();
            Entity m99 = world.Create();
            harness.Ownership.EnsureOwnership(harness.P1Rep, m01);
            harness.Ownership.EnsureOwnership(harness.P2Rep, m99);

            harness.AddTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();

            harness.Writer.ReplaceRouted(
                harness.P1Rep,
                harness.CommandSourceKeyId,
                stackalloc Entity[] { m01, m99 },
                EntityCollectionSourceKind.UiAcquisition,
                DomainRoutingUnresolvedPolicy.Reject);

            Assert.That(harness.Store.TryGet(harness.P2Rep, harness.CommandSourceKeyId, out EntityCollectionHandle p2Handle), Is.True);
            Span<Entity> rows = stackalloc Entity[4];
            Assert.That(harness.Store.CopyEntities(p2Handle, 0, rows), Is.EqualTo(1), "The proxy write must land in the grantor's own domain.");
            Assert.That(rows[0], Is.EqualTo(m99));

            Span<Entity> members = stackalloc Entity[8];
            int count = harness.View.CopyMembers(harness.P1Rep, harness.CommandSourceKeyId, members);
            Assert.That(members[..count].ToArray(), Is.EqualTo(new[] { m01, m99 }), "The composite view spans both domains while the grant holds.");

            harness.RemoveTag(harness.P2Rep, TriggerTag);
            harness.Runtime.Update();

            count = harness.View.CopyMembers(harness.P1Rep, harness.CommandSourceKeyId, members);
            Assert.That(members[..count].ToArray(), Is.EqualTo(new[] { m01 }), "Revoke shrinks the view without touching the foreign domain.");

            Assert.That(harness.Store.TryGet(harness.P2Rep, harness.CommandSourceKeyId, out p2Handle), Is.True);
            Assert.That(harness.Store.CopyEntities(p2Handle, 0, rows), Is.EqualTo(1), "The grantor's domain keeps its latest maintained state.");
            Assert.That(rows[0], Is.EqualTo(m99));
        }

        private static long MeasureUnchangedTickAllocations(Harness harness)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                harness.Runtime.Update();
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static void AssertCoreAssociationSourcesCarryNoScenarioLiterals()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            string? repoRoot = null;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src")) && Directory.Exists(Path.Combine(dir.FullName, "assets")))
                {
                    repoRoot = dir.FullName;
                    break;
                }

                dir = dir.Parent;
            }

            Assert.That(repoRoot, Is.Not.Null, "Failed to locate repository root from test output directory.");
            string[] sources =
            {
                Path.Combine(repoRoot!, "src", "Core", "Gameplay", "Relationships", "AssociationControlProfileRuntime.cs"),
                Path.Combine(repoRoot!, "src", "Core", "Gameplay", "Relationships", "AssociationControlProfileSystem.cs"),
                Path.Combine(repoRoot!, "src", "Core", "Gameplay", "Relationships", "OwnershipEdgeBuilder.cs"),
                Path.Combine(repoRoot!, "src", "Core", "Gameplay", "Relationships", "Config", "AssociationControlProfileConfig.cs"),
                Path.Combine(repoRoot!, "src", "Core", "Gameplay", "Relationships", "Config", "AssociationControlProfilePipelineLoader.cs"),
            };
            string[] forbidden = { "offline", "mind_control", "cinematic", "handback", "policy" };
            foreach (string source in sources)
            {
                Assert.That(File.Exists(source), Is.True, $"Missing {source}");
                string text = File.ReadAllText(source);
                foreach (string token in forbidden)
                {
                    Assert.That(
                        text.Contains(token, StringComparison.OrdinalIgnoreCase),
                        Is.False,
                        $"Core source '{Path.GetFileName(source)}' must not contain scenario literal '{token}'.");
                }
            }
        }

        private static AssociationControlProfileCatalogConfig CreateProxyProfileCatalog()
        {
            return new AssociationControlProfileCatalogConfig
            {
                Profiles =
                {
                    new AssociationControlProfileConfig
                    {
                        Id = "profile.control.test_proxy",
                        When = new AssociationControlConditionConfig
                        {
                            All = new List<AssociationControlConditionConfig>
                            {
                                new() { Relationship = "Ally", Between = new List<string> { "grantee", "grantor" } },
                                new() { Tag = TriggerTag, On = "grantor" },
                            },
                        },
                        Grant = new AssociationControlGrantConfig { EdgeType = "Controls", From = "grantee", To = "grantor" },
                        RevokeWhen = new AssociationControlConditionConfig
                        {
                            Not = new AssociationControlConditionConfig { Tag = TriggerTag, On = "grantor" },
                        },
                    },
                },
            };
        }

        private sealed class Harness
        {
            public World World = null!;
            public RelationshipRuntime Relationships = null!;
            public OwnershipResolver Ownership = null!;
            public TagOps TagOps = null!;
            public AssociationControlProfileRuntime Runtime = null!;
            public EntityCollectionStore Store = null!;
            public DomainRoutedCollectionWriter Writer = null!;
            public ControlPlaneView View = null!;
            public Entity P1Rep;
            public Entity P2Rep;
            public int ControlsTypeId;
            public int GrantedFlagId;
            public int CommandSourceKeyId;

            public static Harness Create(World world, AssociationControlProfileCatalogConfig catalog, bool linkAllies = true)
            {
                var types = new RelationshipTypeRegistry();
                var flags = new RelationshipFlagRegistry();
                var relationships = new RelationshipRuntime(
                    world,
                    types,
                    new RelationshipMetricRegistry(),
                    flags,
                    new RelationshipBandRegistry(),
                    new RelationshipChangeBuffer(capacity: 16),
                    new RelationshipReverseIndex(world));
                int ownsTypeId = types.Register("Owns");
                int controlsTypeId = types.Register("Controls");
                types.Register("MemberOf");
                int allyTypeId = types.Register("Ally", isSymmetric: true);
                int grantedFlagId = flags.Register(AssociationControlProfileRuntime.GrantedFlagName);
                var tagOps = new TagOps();
                var runtime = AssociationControlProfileRuntime.Create(world, relationships, tagOps, types, catalog, grantedFlagId);

                Entity p1Rep = world.Create(
                    new PlayerIdentity { PlayerId = 1 },
                    new GameplayTagContainer(),
                    new TagCountContainer());
                Entity p2Rep = world.Create(
                    new PlayerIdentity { PlayerId = 2 },
                    new GameplayTagContainer(),
                    new TagCountContainer());
                if (linkAllies)
                {
                    relationships.EnsureLink(p1Rep, p2Rep, allyTypeId);
                    relationships.EnsureLink(p2Rep, p1Rep, allyTypeId);
                }

                var ownership = new OwnershipResolver(relationships, ownsTypeId);
                var domains = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId);
                var keyRegistry = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var store = new EntityCollectionStore(keyRegistry, initialCollectionCapacity: 16, initialRowCapacity: 128);
                return new Harness
                {
                    World = world,
                    Relationships = relationships,
                    Ownership = ownership,
                    TagOps = tagOps,
                    Runtime = runtime,
                    Store = store,
                    Writer = new DomainRoutedCollectionWriter(store, domains),
                    View = new ControlPlaneView(store, domains),
                    P1Rep = p1Rep,
                    P2Rep = p2Rep,
                    ControlsTypeId = controlsTypeId,
                    GrantedFlagId = grantedFlagId,
                    CommandSourceKeyId = keyRegistry.Register(EntityCollectionKeys.CommandSource),
                };
            }

            public void AddTag(Entity entity, string tag)
            {
                int tagId = TagRegistry.GetId(tag);
                if (tagId <= 0)
                {
                    tagId = TagRegistry.Register(tag);
                }

                ref GameplayTagContainer tags = ref World.Get<GameplayTagContainer>(entity);
                ref TagCountContainer counts = ref World.Get<TagCountContainer>(entity);
                Assert.That(TagOps.AddTag(ref tags, ref counts, tagId), Is.True);
            }

            public void RemoveTag(Entity entity, string tag)
            {
                int tagId = TagRegistry.GetId(tag);
                Assert.That(tagId, Is.GreaterThan(0));
                ref GameplayTagContainer tags = ref World.Get<GameplayTagContainer>(entity);
                ref TagCountContainer counts = ref World.Get<TagCountContainer>(entity);
                Assert.That(TagOps.RemoveTag(ref tags, ref counts, tagId), Is.True);
            }
        }
    }
}
