using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class PerformContractsAndLegacyLaneTests
    {
        private static readonly string[] ForbiddenPerformerCommandTokens =
        {
            "PresentationCommand",
            "PresentationCommandKind",
            "PresentationCommandBuffer",
            "PerformCommand",
            "PerformCommandBuffer",
        };

        [Test]
        public void PerformerCommand_IsTheOnlyCommandChainSsot()
        {
            Assembly assembly = typeof(PerformerCommand).Assembly;
            Type?[] requiredTypes =
            {
                assembly.GetType("Ludots.Core.Presentation.Performers.PerformerCommand", throwOnError: false),
                assembly.GetType("Ludots.Core.Presentation.Performers.PerformerCommandKind", throwOnError: false),
                assembly.GetType("Ludots.Core.Presentation.Performers.PerformerCommandBuffer", throwOnError: false),
            };
            string[] missingTypes = requiredTypes
                .Where(static type => type == null)
                .Select((_, index) => index switch
                {
                    0 => "PerformerCommand",
                    1 => "PerformerCommandKind",
                    _ => "PerformerCommandBuffer",
                })
                .ToArray();

            Assert.That(
                missingTypes,
                Is.Empty,
                "T3 command-chain rewrite requires PerformerCommand, PerformerCommandKind, and PerformerCommandBuffer to land together.");
        }

        [Test]
        public void LegacyCommandTypes_AreRemovedFromCore()
        {
            Assembly assembly = typeof(PerformerCommand).Assembly;
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Commands.PresentationCommand", throwOnError: false), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Commands.PresentationCommandKind", throwOnError: false), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Commands.PresentationCommandBuffer", throwOnError: false), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Perform.PerformCommand", throwOnError: false), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Perform.PerformCommandBuffer", throwOnError: false), Is.Null);
        }

        [Test]
        public void PerformerCommandContract_ExposesT3Fields()
        {
            FieldInfo[] fields = typeof(PerformerCommand).GetFields(BindingFlags.Instance | BindingFlags.Public);
            string[] fieldNames = fields.Select(static field => field.Name).ToArray();

            Assert.That(fieldNames, Does.Contain("CommandKind"));
            Assert.That(fieldNames, Does.Contain("PerformerDefinitionId"));
            Assert.That(fieldNames, Does.Contain("ParentHandle"));
            Assert.That(fieldNames, Does.Contain("ScopeTag"));
            Assert.That(fieldNames, Does.Contain("ParamKey"));
            Assert.That(fieldNames, Does.Contain("ParamLane"));
            Assert.That(fieldNames, Does.Contain("ParamValue"));
            Assert.That(fieldNames, Does.Contain("IntValue"));
            Assert.That(fieldNames, Does.Contain("VectorValue"));
            Assert.That(fieldNames, Does.Contain("ValueSource"));
            Assert.That(fieldNames, Does.Contain("TargetBehaviorSlot"));

            foreach (string forbiddenToken in ForbiddenPerformerCommandTokens)
            {
                Assert.That(fieldNames, Does.Not.Contain(forbiddenToken));
            }

            Assert.That(typeof(PerformerCommand).GetField("ScopeId", BindingFlags.Instance | BindingFlags.Public), Is.Null);
            Assert.That(typeof(PerformerCommand).GetField("BehaviorSlot", BindingFlags.Instance | BindingFlags.Public), Is.Null);
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

            Assembly assembly = typeof(PerformerCommand).Assembly;
            Type? legacyPartType = assembly.GetType("Ludots.Core.Presentation.Performers.PerformerPart", throwOnError: false);
            Assert.That(legacyPartType, Is.Null, "Runtime must not introduce a dedicated PerformerPart type.");
        }

        [Test]
        public void PerformerInstanceContract_ExposesT4TreeAndTransformFields()
        {
            FieldInfo[] fields = typeof(PerformerInstance).GetFields(BindingFlags.Instance | BindingFlags.Public);
            string[] fieldNames = fields.Select(static field => field.Name).ToArray();

            Assert.That(fieldNames, Does.Contain("ParentHandle"));
            Assert.That(fieldNames, Does.Contain("FirstChildHandle"));
            Assert.That(fieldNames, Does.Contain("NextSiblingHandle"));
            Assert.That(fieldNames, Does.Contain("BehaviorActiveMask"));
            Assert.That(fieldNames, Does.Contain("TransformSource"));
            Assert.That(fieldNames, Does.Contain("WorldRotation"));
            Assert.That(fieldNames, Does.Contain("WorldScale"));
        }

        [Test]
        public void PerformerDefinitionContract_ExposesWave2DefinitionFields()
        {
            FieldInfo[] fields = typeof(PerformerDefinition).GetFields(BindingFlags.Instance | BindingFlags.Public);
            string[] fieldNames = fields.Select(static field => field.Name).ToArray();

            Assert.That(fieldNames, Does.Contain("Key"));
            Assert.That(fieldNames, Does.Contain("Extends"));
            Assert.That(fieldNames, Does.Contain("Children"));
            Assert.That(fieldNames, Does.Contain("Behaviors"));
            Assert.That(fieldNames, Does.Contain("ParamDefaults"));
        }

        [Test]
        public void PerformerScopeTagRegistry_ProvidesStringToIntSsot()
        {
            PerformerScopeTagRegistry.Clear();

            int working = PerformerScopeTagRegistry.Register("working");
            int structure = PerformerScopeTagRegistry.Register("structure");

            Assert.That(working, Is.GreaterThan(0));
            Assert.That(structure, Is.GreaterThan(0));
            Assert.That(PerformerScopeTagRegistry.GetId("working"), Is.EqualTo(working));
            Assert.That(PerformerScopeTagRegistry.GetName(structure), Is.EqualTo("structure"));
        }

        [Test]
        public void TransformSourceContract_MatchesArchitectureValues()
        {
            Assert.That((byte)TransformSource.InheritParent, Is.EqualTo(0));
            Assert.That((byte)TransformSource.EntityTransform, Is.EqualTo(1));
            Assert.That((byte)TransformSource.SplineDriven, Is.EqualTo(2));
            Assert.That((byte)TransformSource.BoneAttached, Is.EqualTo(3));
            Assert.That((byte)TransformSource.WorldFixed, Is.EqualTo(4));
        }

        [Test]
        public void PerformerCommandValueSourceContract_MatchesArchitectureValues()
        {
            Assert.That((byte)PerformerCommandValueSource.Fixed, Is.EqualTo(0));
            Assert.That((byte)PerformerCommandValueSource.EventKeyId, Is.EqualTo(1));
            Assert.That((byte)PerformerCommandValueSource.EventPayloadA, Is.EqualTo(2));
            Assert.That((byte)PerformerCommandValueSource.EventPayloadB, Is.EqualTo(3));
            Assert.That((byte)PerformerCommandValueSource.EventMagnitude, Is.EqualTo(4));
        }

        [Test]
        public void PresentationRequest_RemainsAdapterNeutralOutputGate()
        {
            FieldInfo[] fields = typeof(PresentationRequest).GetFields(BindingFlags.Instance | BindingFlags.Public);

            string[] forbiddenTokens =
            {
                "PerformerRule",
                "PerformerCommand",
                "PerformerCommandBuffer",
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
        public void VisualRenderPayloadContract_ExposesOnlySharedThirteenFields()
        {
            string[] fieldNames = typeof(VisualRenderPayload)
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(static field => field.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            string[] expected =
            {
                "AnimationOverlay",
                "AnimationProfileId",
                "Animator",
                "Color",
                "MaterialId",
                "MeshAssetId",
                "Position",
                "RenderPath",
                "Rotation",
                "Scale",
                "StableId",
                "TemplateId",
                "Visibility",
            };

            Assert.That(fieldNames, Is.EqualTo(expected));
            Assert.That(typeof(PresentationVisualProxy).GetField("LOD", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
            Assert.That(typeof(PrimitiveDrawItem).GetField("LOD", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
            Assert.That(typeof(SkinnedVisualBatchItem).GetField("LOD", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
        }

        [Test]
        public void VisualRenderPayloadContainers_StoreSharedStateOnlyThroughPayload()
        {
            string[] sharedPayloadFields = typeof(VisualRenderPayload)
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(static field => field.Name)
                .ToArray();

            AssertPayloadContainer(typeof(PresentationVisualProxy), sharedPayloadFields, "ProxyKind", "Payload", "Mobility", "Flags", "LOD");
            AssertPayloadContainer(typeof(PrimitiveDrawItem), sharedPayloadFields, "Payload", "Mobility", "Flags", "LOD");
            AssertPayloadContainer(typeof(SkinnedVisualBatchItem), sharedPayloadFields, "Payload", "LOD");
        }

        [Test]
        public void PresentationVisualProxyEmitter_AssignsPayloadAsWhole()
        {
            string repoRoot = FindRepoRoot();
            string sourcePath = Path.Combine(repoRoot, "src", "Core", "Presentation", "Rendering", "PresentationVisualProxyEmitter.cs");
            string source = File.ReadAllText(sourcePath);

            Assert.That(source, Does.Contain("Payload = proxy.Payload"));
        }

        [Test]
        public void LegacyPerformerVisualKindAndBuiltinDefinitions_AreRemovedFromCore()
        {
            Assembly assembly = typeof(PerformerCommand).Assembly;

            Assert.That(assembly.GetType("Ludots.Core.Presentation.Performers.PerformerVisualKind", throwOnError: false), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Performers.BuiltinPerformerDefinitions", throwOnError: false), Is.Null);
        }

        [Test]
        public void EntityVisualEmitSystem_DoesNotDependOnPerformContracts()
        {
            string repoRoot = FindRepoRoot();
            string sourcePath = Path.Combine(repoRoot, "src", "Core", "Presentation", "Systems", "EntityVisualEmitSystem.cs");
            Assert.That(File.Exists(sourcePath), Is.True, $"Missing: {sourcePath}");

            string source = File.ReadAllText(sourcePath);

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

        private static void AssertPayloadContainer(Type containerType, string[] sharedPayloadFields, params string[] expectedPublicFields)
        {
            string[] fieldNames = containerType
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(static field => field.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                fieldNames,
                Is.EqualTo(expectedPublicFields.OrderBy(static name => name, StringComparer.Ordinal).ToArray()),
                $"{containerType.Name} should expose only its payload wrapper fields.");

            string[] duplicatedSharedFields = fieldNames
                .Where(name => sharedPayloadFields.Contains(name, StringComparer.Ordinal))
                .ToArray();

            Assert.That(
                duplicatedSharedFields,
                Is.Empty,
                $"{containerType.Name} must not reintroduce duplicated VisualRenderPayload fields outside Payload.");
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
