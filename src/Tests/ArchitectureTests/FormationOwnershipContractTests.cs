using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using Ludots.Core.MassNavigation.Formation;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class FormationOwnershipContractTests
    {
        private const string FormationShowcaseRoot = "mods/showcases/formation_capability/FormationCapabilityShowcaseMod";
        private const string OptionalFormationCapabilityRoot = "src/Core/MassNavigation/Formation";

        private static readonly string[] FormationOrderKeys =
        {
            "formationMove",
            "formationRotate",
        };

        private static readonly string[] FormationDomainTokens =
        {
            "FormationMember",
            "FormationSlot",
            "FormationPlan",
            "FormationPose",
            "FormationOrder",
            "FormationTarget",
        };

        [Test]
        public void OptionalFormationCapabilityBoundary_AllowsDomainVocabularyOnlyThere()
        {
            string repoRoot = FindRepoRoot();
            string massNavigationRoot = Path.Combine(repoRoot, "src", "Core", "MassNavigation");
            string formationCapabilityRoot = Path.Combine(repoRoot, OptionalFormationCapabilityRoot.Replace('/', Path.DirectorySeparatorChar));

            Assert.That(
                Directory.Exists(formationCapabilityRoot),
                Is.True,
                "Formation is an optional MassNavigation core capability and must have a named boundary instead of living only in the Showcase.");

            List<string> hits = EnumerateFiles(massNavigationRoot, ".cs", ".json")
                .SelectMany(file => FindFormationVocabularyHits(repoRoot, file))
                .Where(hit => !IsUnderRepositoryRoot(hit.Split(':')[0], OptionalFormationCapabilityRoot))
                .ToList();

            Assert.That(
                hits,
                Is.Empty,
                "Generic MassNavigation solver, route, group and runtime layers must not own Formation vocabulary; only the optional Formation capability boundary may do so:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void FormationCapabilityBoundary_DoesNotOwnShowcaseInputOrPresentationPolicy()
        {
            string repoRoot = FindRepoRoot();
            string formationCapabilityRoot = Path.Combine(repoRoot, OptionalFormationCapabilityRoot.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(Directory.Exists(formationCapabilityRoot), Is.True);

            string[] forbiddenTokens =
            {
                "FormationCapability_RotateLeft",
                "FormationCapability_RotateRight",
                "<Keyboard>/q",
                "<Keyboard>/e",
                "PressedThisFrame",
                "IInputActionReader",
                "AuthoritativeInput",
                "RotateStepRadians",
                "MathF.PI / 8f",
                "Camera",
                "HUD",
                "Hud",
                "Outline",
                "Color",
                "Performer",
                "Presentation",
                "Showcase",
                "MassNavigationGroupRuntime",
                "SelectionRuntime",
            };

            List<string> hits = EnumerateFiles(formationCapabilityRoot, ".cs", ".json")
                .SelectMany(file => FindTokenHits(repoRoot, file, forbiddenTokens))
                .ToList();

            Assert.That(
                hits,
                Is.Empty,
                "Optional Formation core capability must stay reusable: no physical keys, local input policy, camera/HUD/outline/color/presentation/showcase glue, Selection, or private MassNavigation group runtime access:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void FormationSemanticOrderKeys_AreOwnedByCapabilityOrShowcaseOnly()
        {
            string repoRoot = FindRepoRoot();
            var hitsByKey = FormationOrderKeys.ToDictionary(
                key => key,
                _ => new List<string>(),
                StringComparer.Ordinal);

            foreach (string file in EnumerateRepositorySourceAndConfigFiles(repoRoot))
            {
                string relative = ToRepoRelativePath(repoRoot, file);
                if (IsTestPath(relative))
                {
                    continue;
                }

                string text = File.ReadAllText(file);
                foreach (string key in FormationOrderKeys)
                {
                    if (ContainsIdentifierToken(text, key))
                    {
                        hitsByKey[key].Add(relative);
                    }
                }
            }

            foreach (string key in FormationOrderKeys)
            {
                Assert.That(
                    hitsByKey[key],
                    Is.Not.Empty,
                    $"Formation showcase must explicitly author its '{key}' order contract.");

                string[] offenders = hitsByKey[key]
                    .Where(path => !IsUnderRepositoryRoot(path, FormationShowcaseRoot) &&
                                   !IsUnderRepositoryRoot(path, OptionalFormationCapabilityRoot))
                    .ToArray();
                Assert.That(
                    offenders,
                    Is.Empty,
                    $"'{key}' is Formation semantic order vocabulary and must stay inside the optional Formation capability or its Showcase adapter:" +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, offenders));
            }
        }

        [Test]
        public void FormationSemanticOrderConsumer_IsOwnedByOptionalCoreCapability()
        {
            string repoRoot = FindRepoRoot();
            string showcaseRoot = Path.Combine(repoRoot, FormationShowcaseRoot.Replace('/', Path.DirectorySeparatorChar));
            string formationCapabilityRoot = Path.Combine(repoRoot, OptionalFormationCapabilityRoot.Replace('/', Path.DirectorySeparatorChar));

            List<string> showcaseOrderConsumers = EnumerateFiles(showcaseRoot, ".cs")
                .SelectMany(file => FindTokenHits(repoRoot, file, new[] { "class FormationCapabilityOrderSystem" }))
                .ToList();

            Assert.That(
                showcaseOrderConsumers,
                Is.Empty,
                "Formation semantic order consumption belongs to the optional Core Formation capability; the Showcase may submit orders but must not own the order consumer:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, showcaseOrderConsumers));

            string formationOrderSystem = Path.Combine(formationCapabilityRoot, "FormationOrderSystem.cs");
            Assert.That(
                File.Exists(formationOrderSystem),
                Is.True,
                "Optional Core Formation capability must own the semantic formation order consumer.");

            string text = File.ReadAllText(formationOrderSystem);
            Assert.That(
                text,
                Does.Contain("public sealed class FormationOrderSystem"),
                "Optional Core Formation capability must expose the semantic formation order consumer as its own system boundary.");
        }

        [Test]
        public void FormationFormalEcsComponents_DoNotStoreFloatGameplayState()
        {
            Type[] componentTypes =
            {
                typeof(FormationCommandState),
                typeof(FormationAnchorState),
                typeof(FormationMemberState),
                typeof(FormationRuntimeState),
            };

            Type[] forbiddenNumericTypes =
            {
                typeof(float),
                typeof(double),
                typeof(Vector2),
                typeof(Vector3),
            };

            List<string> hits = componentTypes
                .SelectMany(type => FindForbiddenNumericMembers(type, forbiddenNumericTypes))
                .ToList();

            Assert.That(
                hits,
                Is.Empty,
                "Formal Formation ECS components are long-lived gameplay truth and must store encoded integer/fixed-point values; planner DTOs may use float only as immediate math:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void FormationMoveBatchClassifier_UsesPreallocatedIndexedLookup()
        {
            string repoRoot = FindRepoRoot();
            string formationOrderSystem = Path.Combine(
                repoRoot,
                OptionalFormationCapabilityRoot.Replace('/', Path.DirectorySeparatorChar),
                "FormationOrderSystem.cs");

            Assert.That(File.Exists(formationOrderSystem), Is.True);

            string text = File.ReadAllText(formationOrderSystem);
            Assert.That(
                text,
                Does.Contain("_moveBatchHashSlots"),
                "Formation move batch classification runs every order tick; it must use a preallocated indexed lookup instead of scanning all existing batches for every actor.");
            Assert.That(
                text,
                Does.Not.Contain("for (int i = 0; i < _moveBatchCount; i++)"),
                "Formation move batch classification must not degrade into O(N²) when many independent OrderIds share one frame.");
        }

        [Test]
        public void FormationExecutionPlanner_IsOwnedByOptionalCoreCapability()
        {
            string repoRoot = FindRepoRoot();
            string showcaseRoot = Path.Combine(repoRoot, FormationShowcaseRoot.Replace('/', Path.DirectorySeparatorChar));
            string formationCapabilityRoot = Path.Combine(repoRoot, OptionalFormationCapabilityRoot.Replace('/', Path.DirectorySeparatorChar));

            string[] showcasePlannerTokens =
            {
                "ApplyFormationExecutionTargets",
                "new MovePlanExecutionIntent",
                "MassNavigationMovePlanExecutionSink",
            };

            List<string> showcasePlannerHits = EnumerateFiles(showcaseRoot, ".cs")
                .SelectMany(file => FindTokenHits(repoRoot, file, showcasePlannerTokens))
                .ToList();

            Assert.That(
                showcasePlannerHits,
                Is.Empty,
                "Stable Formation member-target planning and MovePlanning execution-target emission belong to the optional Core Formation capability; the Showcase may submit semantic orders and render presentation only:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, showcasePlannerHits));

            string executionSystem = Path.Combine(formationCapabilityRoot, "FormationExecutionTargetSystem.cs");
            Assert.That(
                File.Exists(executionSystem),
                Is.True,
                "Optional Core Formation capability must own the system that turns formation pose/member slots into MovePlanning execution targets.");

            string text = File.ReadAllText(executionSystem);
            Assert.That(
                text,
                Does.Contain("public sealed class FormationExecutionTargetSystem"));
            Assert.That(
                text,
                Does.Contain("MovePlanExecutionIntent"));
        }

        [Test]
        public void FormationGovernanceDocs_StayAlignedWithOptionalCoreDecision()
        {
            string repoRoot = FindRepoRoot();
            string entitySimulationUat = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "architecture", "entity-simulation-uat.md"));
            string userBook = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "reference", "mass-navigation-user-book.md"));
            string formalChain = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "reference", "mass-navigation-formal-chain.md"));
            string layering = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "architecture", "entity-simulation-layering.md"));

            Assert.Multiple(() =>
            {
                Assert.That(layering, Does.Contain("Formation 是可选的 MassNavigation Core capability"));
                Assert.That(formalChain, Does.Contain("Formation is an optional MassNavigation Core capability"));
                Assert.That(userBook, Does.Contain("Formation 是可选的 MassNavigation Core capability"));
                Assert.That(userBook, Does.Not.Contain("Formation 是当前 Showcase 的业务能力，不是一个需要在 Core 中开关的可选模式"));
                Assert.That(entitySimulationUat, Does.Contain("Optional Formation core capability"));
                Assert.That(entitySimulationUat, Does.Contain("当前 headless evidence 不宣称 live render FPS"));
                Assert.That(entitySimulationUat, Does.Not.Contain("2k、5k、10k 三档"));
                Assert.That(entitySimulationUat, Does.Not.Contain("Formation 业务只作为上层目标生产者，不进入 Core 验收口径"));
            });
        }

        [Test]
        public void NonFormationConfigs_DoNotAuthorFormationStateOrOrders()
        {
            string repoRoot = FindRepoRoot();
            var forbiddenTokens = FormationDomainTokens.Concat(FormationOrderKeys).ToArray();
            var hits = new List<string>();

            foreach (string root in new[] { "assets", "mods" })
            {
                string absoluteRoot = Path.Combine(repoRoot, root);
                foreach (string file in EnumerateFiles(absoluteRoot, ".json"))
                {
                    string relative = ToRepoRelativePath(repoRoot, file);
                    if (IsUnderRepositoryRoot(relative, FormationShowcaseRoot))
                    {
                        continue;
                    }

                    string text = File.ReadAllText(file);
                    foreach (string token in forbiddenTokens)
                    {
                        if (ContainsIdentifierToken(text, token))
                        {
                            hits.Add($"{relative}: {token}");
                        }
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Non-Formation default/config assets must not author Formation runtime state or Formation-only orders:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void FormationInputBindings_AreShowcasePolicyOnly()
        {
            string repoRoot = FindRepoRoot();
            string[] formationPhysicalActionTokens =
            {
                "FormationCapability_RotateLeft",
                "FormationCapability_RotateRight",
            };

            List<string> hits = EnumerateRepositorySourceAndConfigFiles(repoRoot)
                .Where(file => !IsTestPath(ToRepoRelativePath(repoRoot, file)))
                .SelectMany(file => FindTokenHits(repoRoot, file, formationPhysicalActionTokens))
                .ToList();

            string[] offenders = hits
                .Where(hit => !IsUnderRepositoryRoot(hit.Split(':')[0], FormationShowcaseRoot))
                .ToArray();

            Assert.That(
                offenders,
                Is.Empty,
                "Physical Formation input ids and showcase rotate-step policy must be authored only by the Showcase adapter:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, offenders));

            string[] codeHardcoding = hits
                .Where(hit => hit.Split(':')[0].EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.That(
                codeHardcoding,
                Is.Empty,
                "Formation physical action ids and rotate-step policy must come from Showcase input/config assets, not runtime code constants:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, codeHardcoding));

            string[] formationRotateStepCodeHardcoding = EnumerateFiles(
                    Path.Combine(repoRoot, FormationShowcaseRoot.Replace('/', Path.DirectorySeparatorChar)),
                    ".cs")
                .SelectMany(file => FindTokenHits(repoRoot, file, new[] { "MathF.PI / 8f" }))
                .ToArray();

            Assert.That(
                formationRotateStepCodeHardcoding,
                Is.Empty,
                "Formation rotate-step policy must be authored by Showcase input/config assets, not runtime code constants:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, formationRotateStepCodeHardcoding));
        }

        [Test]
        public void FormationShowcaseSystems_DoNotCaptureMassNavigationRuntimeInstances()
        {
            string repoRoot = FindRepoRoot();
            string showcaseRuntimeRoot = Path.Combine(repoRoot, FormationShowcaseRoot.Replace('/', Path.DirectorySeparatorChar), "Runtime");
            string[] forbiddenTokens =
            {
                "private readonly MassNavigationSimulationRuntime _simulation",
                "new FormationCapabilityShowcaseFormationRuntimeSystem(engine, this, simulation)",
                "new FormationCapabilityShowcaseScenarioBindingSystem(engine, this, simulation)",
            };

            List<string> hits = EnumerateFiles(showcaseRuntimeRoot, ".cs")
                .SelectMany(file => FindTokenHits(repoRoot, file, forbiddenTokens))
                .ToList();

            Assert.That(
                hits,
                Is.Empty,
                "Formation showcase systems must resolve the prepared MassNavigation runtime through RuntimeBinding instead of retaining a stale runtime instance across suspend/resume or reload:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        private static IEnumerable<string> FindFormationVocabularyHits(string repoRoot, string file)
        {
            string relative = ToRepoRelativePath(repoRoot, file);
            if (Path.GetFileName(file).Contains("Formation", StringComparison.Ordinal))
            {
                yield return relative + ": file name";
            }

            foreach ((int lineNumber, string line) in SourceTextScanner.ReadCodeLines(file))
            {
                if (line.Contains("Formation", StringComparison.Ordinal) ||
                    Regex.IsMatch(line, @"\bformation\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    yield return $"{relative}:{lineNumber}: {line.Trim()}";
                }
            }
        }

        private static IEnumerable<string> FindTokenHits(string repoRoot, string file, IReadOnlyList<string> tokens)
        {
            string relative = ToRepoRelativePath(repoRoot, file);
            foreach ((int lineNumber, string line) in SourceTextScanner.ReadCodeLines(file))
            {
                for (int i = 0; i < tokens.Count; i++)
                {
                    string token = tokens[i];
                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        yield return $"{relative}:{lineNumber}: {token}";
                    }
                }
            }
        }

        private static bool ContainsIdentifierToken(string text, string token)
        {
            return Regex.IsMatch(
                text,
                $@"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])",
                RegexOptions.CultureInvariant);
        }

        private static IEnumerable<string> FindForbiddenNumericMembers(Type componentType, IReadOnlyList<Type> forbiddenTypes)
        {
            foreach (FieldInfo field in componentType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (forbiddenTypes.Contains(field.FieldType))
                {
                    yield return $"{componentType.Name}.{field.Name}: {field.FieldType.Name}";
                }
            }

            foreach (PropertyInfo property in componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (forbiddenTypes.Contains(property.PropertyType))
                {
                    yield return $"{componentType.Name}.{property.Name}: {property.PropertyType.Name}";
                }
            }
        }

        private static IEnumerable<string> EnumerateRepositorySourceAndConfigFiles(string repoRoot)
        {
            foreach (string root in new[] { "src", "mods", "assets" })
            {
                foreach (string file in EnumerateFiles(Path.Combine(repoRoot, root), ".cs", ".json"))
                {
                    yield return file;
                }
            }
        }

        private static IEnumerable<string> EnumerateFiles(string root, params string[] extensions)
        {
            Assert.That(Directory.Exists(root), Is.True, $"Missing repository directory: {root}");
            var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            var excludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git",
                ".tmp",
                "artifacts",
                "bin",
                "obj",
            };

            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => extensionSet.Contains(Path.GetExtension(file)))
                .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => excludedDirectories.Contains(segment)));
        }

        private static bool IsTestPath(string relativePath)
        {
            return IsUnderRepositoryRoot(relativePath, "src/Tests");
        }

        private static bool IsUnderRepositoryRoot(string relativePath, string root)
        {
            return relativePath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                   relativePath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
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

        private static string ToRepoRelativePath(string repoRoot, string file)
        {
            return Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
        }
    }
}
