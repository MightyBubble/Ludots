using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Perform;
using Ludots.Core.Presentation.Requests;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class PerformContractsAndLegacyLaneTests
    {
        private static readonly string[] MandatoryPerformContractNames =
        {
            "PerformAudienceContext",
            "PerformPhaseInput",
            "PerformPhaseResult",
            "PerformPhaseResolver",
        };

        private static readonly string[] ForbiddenPhaseDependencyTokens =
        {
            "SelectionRequestQueue",
            "SelectionResponseBuffer",
            "SelectionRuntime",
            "SelectionView",
            "PresentationRequest",
            "PresentationRequestBuffer",
            "PresentationRequestFlushSystem",
            "Flush",
        };

        [Test]
        public void PerformContracts_KeepExistingBaseContracts_AndDefineSingleNextStepForMandatoryPhaseContracts()
        {
            Type[] existingContractTypes =
            {
                typeof(PerformBehaviorDefinition),
                typeof(PerformBehaviorInstance),
                typeof(PerformBehaviorKind),
                typeof(PerformCommand),
                typeof(PerformRule),
            };

            Assert.That(
                existingContractTypes.Select(static type => type.Namespace).Distinct().ToArray(),
                Is.EqualTo(new[] { "Ludots.Core.Presentation.Perform" }));

            Assembly assembly = typeof(PerformRule).Assembly;
            string[] missingMandatoryContracts = MandatoryPerformContractNames
                .Where(name => assembly.GetType($"Ludots.Core.Presentation.Perform.{name}", throwOnError: false) == null)
                .ToArray();

            if (missingMandatoryContracts.Length == MandatoryPerformContractNames.Length)
            {
                Assert.Pass("Mandatory phase contracts are not landed yet; additive first-wave compatibility remains intact.");
            }

            Assert.That(
                missingMandatoryContracts,
                Is.Empty,
                "Once phase-contract migration starts, the full mandatory minimal phase contract set must land together.");
        }

        [Test]
        public void PerformContracts_DoNotExposeLegacyVisibilityAlias()
        {
            Assembly assembly = typeof(PerformRule).Assembly;
            Type? legacyAlias = assembly.GetType("Ludots.Core.Presentation.Perform.PerformVisibilityInput", throwOnError: false);
            Assert.That(legacyAlias, Is.Null, "PerformVisibilityInput must not exist. The SSOT contract is PerformAudienceContext + PerformPhaseInput + PerformPhaseResult.");
        }

        [Test]
        public void MandatoryPhaseContracts_AreDefinedInPerformNamespaceOnly()
        {
            Assembly assembly = typeof(PerformRule).Assembly;
            Type?[] contractTypes = MandatoryPerformContractNames
                .Select(name => assembly.GetType($"Ludots.Core.Presentation.Perform.{name}", throwOnError: false))
                .ToArray();

            if (contractTypes.All(static type => type == null))
            {
                Assert.Pass("Mandatory phase contracts are not landed yet; namespace enforcement will apply when they appear.");
            }

            string[] namespaces = contractTypes
                .Where(static type => type != null)
                .Select(static type => type!.Namespace ?? string.Empty)
                .Distinct()
                .ToArray();

            Assert.That(namespaces, Is.EqualTo(new[] { "Ludots.Core.Presentation.Perform" }));
        }

        [Test]
        public void MandatoryPhaseContracts_DoNotDependOnSelectionOrRequestFlush()
        {
            Assembly assembly = typeof(PerformRule).Assembly;
            Type?[] contractTypes = MandatoryPerformContractNames
                .Select(name => assembly.GetType($"Ludots.Core.Presentation.Perform.{name}", throwOnError: false))
                .ToArray();

            foreach (Type? contractType in contractTypes)
            {
                Assert.That(contractType, Is.Not.Null, "Mandatory phase contract migration must land as a complete set.");

                List<string> offenders = EnumerateForbiddenMemberDependencies(contractType!)
                    .Where(token => ForbiddenPhaseDependencyTokens.Any(forbidden =>
                        token.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                Assert.That(
                    offenders,
                    Is.Empty,
                    $"{contractType!.Name} must not depend on selection state or request flush contracts.");
            }
        }

        [Test]
        public void PerformPhaseResolver_SourceDoesNotReferenceSelectionOrRequestFlush()
        {
            string repoRoot = FindRepoRoot();
            string sourcePath = Path.Combine(repoRoot, "src", "Core", "Presentation", "Perform", "PerformPhaseResolver.cs");

            if (!File.Exists(sourcePath))
            {
                Assert.Pass("PerformPhaseResolver source is not landed yet; source guard becomes active when the file appears.");
            }

            string source = File.ReadAllText(sourcePath);

            foreach (string forbiddenToken in ForbiddenPhaseDependencyTokens)
            {
                Assert.That(
                    source,
                    Does.Not.Contain(forbiddenToken),
                    $"PerformPhaseResolver must not couple to selection/request flush token '{forbiddenToken}'.");
            }
        }

        [Test]
        public void PrefabPart_RemainsStaticAssetOnlyContract()
        {
            FieldInfo[] fields = typeof(PrefabPart).GetFields(BindingFlags.Instance | BindingFlags.Public);

            string[] offendingNames =
                fields.Where(static field =>
                        field.Name.Contains("Behavior", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Contains("Animator", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Contains("Command", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Contains("Request", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Contains("Viewer", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Contains("Visibility", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Contains("Audience", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Contains("Phase", StringComparison.OrdinalIgnoreCase))
                    .Select(static field => field.Name)
                    .ToArray();

            string[] offendingTypes =
                fields.Where(static field =>
                        field.FieldType.Namespace != null &&
                        field.FieldType.Namespace.StartsWith("Ludots.Core.Presentation.Perform", StringComparison.Ordinal))
                    .Select(static field => $"{field.Name}:{field.FieldType.FullName}")
                    .ToArray();

            Assert.That(offendingNames, Is.Empty, "PrefabPart must stay an asset-only contract.");
            Assert.That(offendingTypes, Is.Empty, "PrefabPart must not depend on perform orchestration contracts.");
        }

        [Test]
        public void PerformerRuntime_UsesExistingScopeAndParamFlow_InsteadOfDedicatedPartType()
        {
            Type performerInstance = typeof(Ludots.Core.Presentation.Performers.PerformerInstance);
            FieldInfo? scopeField = performerInstance.GetField("ScopeId", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(scopeField, Is.Not.Null, "Performer runtime should continue using ScopeId for grouping.");

            Assembly assembly = typeof(PerformRule).Assembly;
            Type? legacyPartType = assembly.GetType("Ludots.Core.Presentation.Performers.PerformerPart", throwOnError: false);
            Assert.That(legacyPartType, Is.Null, "Runtime must not introduce a dedicated PerformerPart type.");
        }

        [Test]
        public void PresentationRequest_RemainsAdapterNeutralOutputGate()
        {
            FieldInfo[] fields = typeof(PresentationRequest).GetFields(BindingFlags.Instance | BindingFlags.Public);

            string[] forbiddenTokens =
            {
                "PerformerRule",
                "PerformerCommand",
                "PerformRule",
                "PerformCommand",
                "PerformBehavior",
                "PerformAudienceContext",
                "PerformPhaseInput",
                "PerformPhaseResult",
                "PerformPhaseResolver",
                "PresentationBehaviorDefinition",
            };

            string[] offendingNames =
                fields.Where(field => forbiddenTokens.Any(token => field.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    .Select(static field => field.Name)
                    .ToArray();

            string[] offendingTypes =
                fields.Where(field =>
                    {
                        string? fullName = field.FieldType.FullName;
                        return fullName != null &&
                               forbiddenTokens.Any(token => fullName.Contains(token, StringComparison.OrdinalIgnoreCase));
                    })
                    .Select(static field => $"{field.Name}:{field.FieldType.FullName}")
                    .ToArray();

            Assert.That(offendingNames, Is.Empty, "PresentationRequest must stay an adapter-neutral output packet.");
            Assert.That(offendingTypes, Is.Empty, "PresentationRequest must not store orchestration or phase contracts.");
        }

        [Test]
        public void EntityVisualEmitSystem_DoesNotDependOnPerformContracts()
        {
            string repoRoot = FindRepoRoot();
            string sourcePath = Path.Combine(repoRoot, "src", "Core", "Presentation", "Systems", "EntityVisualEmitSystem.cs");
            Assert.That(File.Exists(sourcePath), Is.True, $"Missing: {sourcePath}");

            string source = File.ReadAllText(sourcePath);

            string normalized = source.Replace("using Ludots.Core.Presentation.Perform;", string.Empty, StringComparison.Ordinal);

            Assert.That(normalized, Does.Not.Contain("PerformAudienceContext"));
            Assert.That(normalized, Does.Not.Contain("PerformPhaseInput"));
            Assert.That(normalized, Does.Not.Contain("PerformPhaseResult"));
            Assert.That(normalized, Does.Not.Contain("PerformPhaseResolver"));
            Assert.That(source, Does.Not.Contain("PerformBehavior"));
            Assert.That(source, Does.Not.Contain("PerformCommand"));
            Assert.That(source, Does.Not.Contain("PerformRule"));
            Assert.That(source, Does.Not.Contain("PerformerInstance"));
        }

        private static IEnumerable<string> EnumerateForbiddenMemberDependencies(Type contractType)
        {
            foreach (FieldInfo field in contractType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                yield return field.Name;
                yield return field.FieldType.FullName ?? field.FieldType.Name;
            }

            foreach (PropertyInfo property in contractType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                yield return property.Name;
                yield return property.PropertyType.FullName ?? property.PropertyType.Name;
            }

            foreach (ConstructorInfo constructor in contractType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    yield return parameter.Name ?? string.Empty;
                    yield return parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
                }
            }

            foreach (MethodInfo method in contractType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                yield return method.Name;
                yield return method.ReturnType.FullName ?? method.ReturnType.Name;
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    yield return parameter.Name ?? string.Empty;
                    yield return parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
                }
            }
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }
    }
}
