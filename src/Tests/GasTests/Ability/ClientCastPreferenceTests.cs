using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 CTX-8 (§5.6, §6.2 P1/P2): ClientCastPreference scope chain — perSlot &gt; perFormSet
    /// &gt; perTemplate &gt; global resolution, mod locks overriding every player layer and refusing
    /// player writes, JSON persistence roundtrip (following the InputOrderMappingSystem preference
    /// file pattern), and load-time fail-fast on uninstalled cast commit ids. Profile/template names
    /// are test data, never Core concepts.
    /// </summary>
    [TestFixture]
    public sealed class ClientCastPreferenceTests
    {
        private const string QuickProfile = "cast.commit.quick";
        private const string AimConfirmProfile = "cast.commit.aim_confirm";
        private const string QuickIndicatorProfile = "cast.commit.quick_with_indicator";
        private const string XerathTemplate = "champion.xerath";
        private const string JayceTemplate = "champion.jayce";
        private const string HammerFormSet = "jayce_forms/hammer";

        [Test]
        public void Resolve_ScopeChain_DeeperScopesWin()
        {
            Harness harness = Harness.Create();
            int xerath = harness.TemplateId(XerathTemplate);
            int jayce = harness.TemplateId(JayceTemplate);
            int hammer = harness.FormSetId(HammerFormSet);

            Assert.That(harness.Store.TrySetPreference(CastPreferenceScope.Global, 0, 0, 0, harness.CastCommit(QuickProfile)), Is.True);
            Assert.That(harness.Store.TrySetPreference(CastPreferenceScope.PerTemplate, xerath, 0, 0, harness.CastCommit(AimConfirmProfile)), Is.True);
            Assert.That(harness.Store.TrySetPreference(CastPreferenceScope.PerSlot, xerath, 0, 2, harness.CastCommit(QuickIndicatorProfile)), Is.True);

            // P2: slot beats template, template beats global, uncovered templates read global.
            Assert.That(harness.Store.ResolveCastCommit(xerath, 0, 2), Is.EqualTo(harness.CastCommit(QuickIndicatorProfile)));
            Assert.That(harness.Store.ResolveCastCommit(xerath, 0, 0), Is.EqualTo(harness.CastCommit(AimConfirmProfile)));
            Assert.That(harness.Store.ResolveCastCommit(jayce, 0, 2), Is.EqualTo(harness.CastCommit(QuickProfile)));

            // Form set layer sits between slot and template.
            Assert.That(harness.Store.TrySetPreference(CastPreferenceScope.PerFormSet, 0, hammer, 0, harness.CastCommit(QuickIndicatorProfile)), Is.True);
            Assert.That(harness.Store.ResolveCastCommit(xerath, hammer, 0), Is.EqualTo(harness.CastCommit(QuickIndicatorProfile)));
            Assert.That(harness.Store.ResolveCastCommit(xerath, hammer, 2), Is.EqualTo(harness.CastCommit(QuickIndicatorProfile)));
            Assert.That(harness.Store.ResolveCastCommit(jayce, 0, 0), Is.EqualTo(harness.CastCommit(QuickProfile)));
        }

        [Test]
        public void Resolve_NoPreference_ReturnsZero_ModDefaultApplies()
        {
            Harness harness = Harness.Create();
            Assert.That(harness.Store.ResolveCastCommit(harness.TemplateId(XerathTemplate), 0, 0), Is.EqualTo(0));
        }

        [Test]
        public void TrySetPreference_UninstalledCastCommit_FailsFast()
        {
            Harness harness = Harness.Create();
            Assert.Throws<InvalidOperationException>(() =>
                harness.Store.TrySetPreference(CastPreferenceScope.Global, 0, 0, 0, castCommitId: 999));
        }

        [Test]
        public void Locks_OverridePlayerLayers_AndRefusePlayerWrites()
        {
            Harness harness = Harness.Create();
            int xerath = harness.TemplateId(XerathTemplate);

            // Player preference written before the mod lock installs; the lock still wins.
            Assert.That(harness.Store.TrySetPreference(CastPreferenceScope.PerSlot, xerath, 0, 3, harness.CastCommit(QuickProfile)), Is.True);
            harness.Store.InstallLocks(new CastCommitLocksConfig
            {
                Locks = new List<CastCommitLockDefinition>
                {
                    new() { Scope = CastPreferenceScopeNames.Slot, Key = $"{XerathTemplate}/3", CastCommitId = AimConfirmProfile },
                },
            });

            Assert.That(harness.Store.ResolveCastCommit(xerath, 0, 3), Is.EqualTo(harness.CastCommit(AimConfirmProfile)));
            Assert.That(harness.Store.IsLocked(CastPreferenceScope.PerSlot, xerath, 0, 3), Is.True);

            // P2 mod-lock scenario: the settings write is refused, behavior stays locked.
            Assert.That(harness.Store.TrySetPreference(CastPreferenceScope.PerSlot, xerath, 0, 3, harness.CastCommit(QuickProfile)), Is.False);
            Assert.That(harness.Store.ResolveCastCommit(xerath, 0, 3), Is.EqualTo(harness.CastCommit(AimConfirmProfile)));

            // Sibling slots stay player controlled.
            Assert.That(harness.Store.TrySetPreference(CastPreferenceScope.PerSlot, xerath, 0, 1, harness.CastCommit(QuickProfile)), Is.True);
            Assert.That(harness.Store.ResolveCastCommit(xerath, 0, 1), Is.EqualTo(harness.CastCommit(QuickProfile)));
        }

        [Test]
        public void InstallLocks_FailFast_OnUnknownIdScopeOrDuplicate()
        {
            Harness harness = Harness.Create();
            Assert.Throws<InvalidOperationException>(() => harness.Store.InstallLocks(new CastCommitLocksConfig
            {
                Locks = new List<CastCommitLockDefinition>
                {
                    new() { Scope = CastPreferenceScopeNames.Global, Key = "", CastCommitId = "cast.commit.not_installed" },
                },
            }));

            Assert.Throws<InvalidOperationException>(() => ClientCastPreferenceConfigLoader.Validate(new CastCommitLocksConfig
            {
                Locks = new List<CastCommitLockDefinition>
                {
                    new() { Scope = "perHero", Key = XerathTemplate, CastCommitId = QuickProfile },
                },
            }, "test"));

            Assert.Throws<InvalidOperationException>(() => harness.Store.InstallLocks(new CastCommitLocksConfig
            {
                Locks = new List<CastCommitLockDefinition>
                {
                    new() { Scope = CastPreferenceScopeNames.Template, Key = XerathTemplate, CastCommitId = QuickProfile },
                    new() { Scope = CastPreferenceScopeNames.Template, Key = XerathTemplate, CastCommitId = AimConfirmProfile },
                },
            }));
        }

        [Test]
        public void SaveLoad_RoundtripsPreferencesAndActiveScheme()
        {
            Harness harness = Harness.Create();
            int xerath = harness.TemplateId(XerathTemplate);
            int hammer = harness.FormSetId(HammerFormSet);
            harness.Store.TrySetPreference(CastPreferenceScope.Global, 0, 0, 0, harness.CastCommit(QuickProfile));
            harness.Store.TrySetPreference(CastPreferenceScope.PerTemplate, xerath, 0, 0, harness.CastCommit(AimConfirmProfile));
            harness.Store.TrySetPreference(CastPreferenceScope.PerFormSet, 0, hammer, 0, harness.CastCommit(QuickIndicatorProfile));
            harness.Store.TrySetPreference(CastPreferenceScope.PerSlot, xerath, 0, 2, harness.CastCommit(QuickIndicatorProfile));
            harness.Store.SetActiveScheme("scheme.test.roundtrip");

            string path = Path.Combine(Path.GetTempPath(), $"ludots_cast_prefs_{Guid.NewGuid():N}.json");
            try
            {
                harness.Store.Save(path);

                // P1: preferences survive a client restart (fresh store over the same id spaces).
                ClientCastPreferenceStore reloaded = harness.CreateStore();
                uint revisionBefore = reloaded.Revision;
                reloaded.Load(path);

                Assert.That(reloaded.Revision, Is.GreaterThan(revisionBefore));
                Assert.That(reloaded.ResolveCastCommit(xerath, 0, 2), Is.EqualTo(harness.CastCommit(QuickIndicatorProfile)));
                Assert.That(reloaded.ResolveCastCommit(xerath, 0, 0), Is.EqualTo(harness.CastCommit(AimConfirmProfile)));
                Assert.That(reloaded.ResolveCastCommit(0, hammer, 0), Is.EqualTo(harness.CastCommit(QuickIndicatorProfile)));
                Assert.That(reloaded.ResolveCastCommit(harness.TemplateId(JayceTemplate), 0, 0), Is.EqualTo(harness.CastCommit(QuickProfile)));
                Assert.That(reloaded.ActiveSchemeId, Is.EqualTo("scheme.test.roundtrip"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Load_UnknownCastCommitId_FailsFast()
        {
            Harness harness = Harness.Create();
            string path = Path.Combine(Path.GetTempPath(), $"ludots_cast_prefs_{Guid.NewGuid():N}.json");
            File.WriteAllText(path, """{ "global": { "castCommitId": "cast.commit.gone" } }""");
            try
            {
                Assert.Throws<InvalidOperationException>(() => harness.Store.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Load_MissingFile_IsEmptyPreferenceSet()
        {
            Harness harness = Harness.Create();
            harness.Store.TrySetPreference(CastPreferenceScope.Global, 0, 0, 0, harness.CastCommit(QuickProfile));

            harness.Store.Load(Path.Combine(Path.GetTempPath(), $"ludots_missing_{Guid.NewGuid():N}.json"));

            Assert.That(harness.Store.ResolveCastCommit(0, 0, 0), Is.EqualTo(0));
            Assert.That(harness.Store.ActiveSchemeId, Is.Empty);
        }

        [Test]
        public void DefaultLocksConfigFile_DeserializesAndValidates()
        {
            string configPath = Path.Combine(FindRepoRoot(), "assets", "Configs", "Input", "cast_commit_locks.json");
            Assert.That(File.Exists(configPath), Is.True, $"Missing {configPath}");

            var config = JsonSerializer.Deserialize<CastCommitLocksConfig>(
                File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.That(config, Is.Not.Null);
            ClientCastPreferenceConfigLoader.Validate(config, "assets");
        }

        [Test]
        public void Resolve_SteadyState_IsAllocationFree()
        {
            Harness harness = Harness.Create();
            int xerath = harness.TemplateId(XerathTemplate);
            int hammer = harness.FormSetId(HammerFormSet);
            harness.Store.TrySetPreference(CastPreferenceScope.Global, 0, 0, 0, harness.CastCommit(QuickProfile));
            harness.Store.TrySetPreference(CastPreferenceScope.PerSlot, xerath, 0, 2, harness.CastCommit(AimConfirmProfile));

            harness.Store.ResolveCastCommit(xerath, hammer, 2);
            long allocated = MeasureResolveAllocations(harness.Store, xerath, hammer);
            allocated = Math.Min(allocated, MeasureResolveAllocations(harness.Store, xerath, hammer));
            Assert.That(allocated, Is.EqualTo(0), "Steady-state preference resolution must be allocation free.");
        }

        private static long MeasureResolveAllocations(ClientCastPreferenceStore store, int templateId, int formSetId)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                store.ResolveCastCommit(templateId, formSetId, 2);
                store.ResolveCastCommit(templateId, formSetId, 0);
                store.ResolveCastCommit(0, 0, 0);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }

        private sealed class Harness
        {
            public ClientCastPreferenceStore Store = null!;
            private CastCommitProfileRegistry _castCommits = null!;
            private StringIntRegistry _castCommitIds = null!;
            private StringIntRegistry _templateKeys = null!;
            private StringIntRegistry _formSetKeys = null!;

            public static Harness Create()
            {
                var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var stack = new InteractionContextStack(collectionKeys);
                var contextProfiles = new InteractionContextProfileRegistry(stack.ContextIdRegistry);
                var castCommitIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var actionIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var castCommits = new CastCommitProfileRegistry(castCommitIds, actionIds, contextProfiles);
                castCommits.Install(new CastCommitProfilesConfig
                {
                    Profiles = new List<CastCommitProfileDefinition>
                    {
                        Profile(QuickProfile),
                        Profile(AimConfirmProfile),
                        Profile(QuickIndicatorProfile),
                    },
                });

                var harness = new Harness
                {
                    _castCommits = castCommits,
                    _castCommitIds = castCommitIds,
                    _templateKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                    _formSetKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                };
                harness.Store = harness.CreateStore();
                return harness;
            }

            /// <summary>A fresh store over the same registries (models a client restart).</summary>
            public ClientCastPreferenceStore CreateStore()
            {
                return new ClientCastPreferenceStore(
                    _castCommits,
                    _templateKeys.Register,
                    _templateKeys.GetName,
                    _formSetKeys.Register,
                    _formSetKeys.GetName);
            }

            public int CastCommit(string name) => _castCommitIds.GetId(name);
            public int TemplateId(string key) => _templateKeys.Register(key);
            public int FormSetId(string key) => _formSetKeys.Register(key);

            private static CastCommitProfileDefinition Profile(string id)
            {
                return new CastCommitProfileDefinition
                {
                    Id = id,
                    OnActivate = new List<CastCommitOpDefinition>
                    {
                        new() { Op = InteractionOpKinds.SubmitOrder },
                    },
                };
            }
        }
    }
}
