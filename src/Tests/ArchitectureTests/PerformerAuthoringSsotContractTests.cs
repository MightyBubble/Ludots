using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ludots.Core.Presentation.Performers;
using Ludots.Launcher.Backend;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class PerformerAuthoringSsotContractTests
    {
        [Test]
        public void EffekseerRuntimeManifest_MatchesCanonicalContractAndLibraryBytes()
        {
            string repoRoot = FindRepoRoot();
            JsonObject contract = ReadObject(Path.Combine(
                repoRoot,
                "src",
                "Libraries",
                "Effekseer",
                "runtime-contract.json"));
            JsonObject upstream = ReadObject(Path.Combine(
                repoRoot,
                "src",
                "Libraries",
                "Effekseer",
                "Effekseer.upstream.json"));
            JsonObject runtime = contract["runtimes"]!.AsArray()
                .Select(node => node!.AsObject())
                .Single();
            string runtimeIdentifier = runtime["runtimeIdentifier"]!.GetValue<string>();
            string runtimeRoot = Path.Combine(
                repoRoot,
                "src",
                "Libraries",
                "Effekseer",
                "runtimes",
                runtimeIdentifier);
            JsonObject manifest = ReadObject(Path.Combine(runtimeRoot, "runtime-manifest.json"));
            XDocument msbuildContract = XDocument.Load(Path.Combine(
                repoRoot,
                "src",
                "Libraries",
                "Effekseer",
                "runtime-contract.props"));

            Assert.Multiple(() =>
            {
                Assert.That(manifest["runtimeIdentifier"]!.GetValue<string>(), Is.EqualTo(runtime["runtimeIdentifier"]!.GetValue<string>()));
                Assert.That(manifest["library"]!.GetValue<string>(), Is.EqualTo(runtime["library"]!.GetValue<string>()));
                Assert.That(manifest["bridgeAbiVersion"]!.GetValue<int>(), Is.EqualTo(contract["bridgeAbiVersion"]!.GetValue<int>()));
                Assert.That(manifest["effekseerRuntimeFormatVersion"]!.GetValue<int>(), Is.EqualTo(contract["runtimeFormatVersion"]!.GetValue<int>()));
                Assert.That(manifest["dynamicInputSlotCount"]!.GetValue<int>(), Is.EqualTo(contract["dynamicInputSlotCount"]!.GetValue<int>()));
                Assert.That(contract["dynamicInputSlotCount"]!.GetValue<int>(), Is.EqualTo(MaterialCustomDataBinding.MaxSlots));
                Assert.That(manifest["effekseerVersion"]!.GetValue<string>(), Is.EqualTo(upstream["version"]!.GetValue<string>()));
                Assert.That(
                    manifest["emitterNodeTypes"]!.ToJsonString(),
                    Is.EqualTo(contract["emitterNodeTypes"]!.ToJsonString()));
                Assert.That(
                    msbuildContract.Descendants("EffekseerSupportedRuntimeIdentifiers").Single().Value.Split(';'),
                    Is.EqualTo(contract["runtimes"]!.AsArray()
                        .Select(node => node!["runtimeIdentifier"]!.GetValue<string>())
                        .ToArray()));
                Assert.That(
                    msbuildContract.Descendants("EffekseerNativeContractLibrary").Single().Value.Replace('\\', '/'),
                    Is.EqualTo(runtime["library"]!.GetValue<string>()));
            });

            string libraryPath = Path.Combine(
                runtimeRoot,
                runtime["library"]!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar));
            string actualSha256;
            using (FileStream stream = File.OpenRead(libraryPath))
            {
                actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }
            Assert.That(manifest["sha256"]!.GetValue<string>(), Is.EqualTo(actualSha256));
        }

        [Test]
        public void CrossLanguageAuthoringContract_MatchesCoreRegistries()
        {
            string repoRoot = FindRepoRoot();
            JsonObject effekseerContract = ReadObject(Path.Combine(
                repoRoot,
                "src",
                "Libraries",
                "Effekseer",
                "runtime-contract.json"));
            string authoringRoot = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "performer_raylib_micro_showcases",
                "PerformerRaylibMicroShowcasesMod",
                "assets",
                "Presentation",
                "Authoring");
            JsonObject authoringContract = ReadObject(Path.Combine(
                authoringRoot,
                "raylib-micro-showcases.contract.json"));
            JsonObject authoring = ReadObject(Path.Combine(
                authoringRoot,
                "raylib-micro-showcases.authoring.json"));

            string[] coreEmitterKinds = Enum.GetValues<AssetKind>()
                .Where(kind => kind.IsEmitterKind())
                .Select(kind => kind.ToString())
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] contractEmitterKinds = effekseerContract["emitterNodeTypes"]!.AsObject()
                .Select(property => property.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] corePrimitiveKinds = Enum.GetValues<AssetKind>()
                .Where(kind => kind.IsConcretePrimitiveKind())
                .Select(kind => kind.ToString())
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] contractPrimitiveKinds = authoringContract["raylibPrimitiveAssetKinds"]!.AsArray()
                .Select(node => node!.GetValue<string>())
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            JsonObject motionSlots = authoringContract["motionBehaviorSlots"]!.AsObject();
            JsonObject recording = authoringContract["recording"]!.AsObject();
            string scaleSlot = motionSlots["scale"]!.GetValue<string>();
            string alphaSlot = motionSlots["alpha"]!.GetValue<string>();
            Assert.Multiple(() =>
            {
                Assert.That(contractEmitterKinds, Is.EqualTo(coreEmitterKinds));
                Assert.That(contractPrimitiveKinds, Is.EqualTo(corePrimitiveKinds));
                Assert.That(authoringContract["maxChildPerformers"]!.GetValue<int>(), Is.EqualTo(PerformerChildren.MAX_CHILDREN));
                Assert.That(scaleSlot, Is.EqualTo(PerformerBehaviorSlotRegistry.MotionScale));
                Assert.That(alphaSlot, Is.EqualTo(PerformerBehaviorSlotRegistry.MotionAlpha));
                Assert.That(PerformerBehaviorSlotRegistry.Resolve(scaleSlot), Is.Not.EqualTo(PerformerBehaviorSlotRegistry.Resolve(alphaSlot)));
                Assert.That(
                    recording["launcherUserConfigEnvironmentVariable"]!.GetValue<string>(),
                    Is.EqualTo(LauncherEnvironmentKeys.UserConfigPath));
            });

            string[] coveredShowcases = authoringContract["showcases"]!.AsArray()
                .Select(node => node!["id"]!.GetValue<string>())
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            string[] authoredShowcases = authoring["showcases"]!.AsArray()
                .Select(node => node!["id"]!.GetValue<string>())
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            Assert.That(authoredShowcases, Is.EqualTo(coveredShowcases));
        }

        [Test]
        public void PerformerArchitectureContract_RejectsRetiredDeadConfigurationAndLayerDrift()
        {
            Assert.That(typeof(BehaviorSlot).GetField("ActivationCondition"), Is.Null);
            Assert.That(Enum.GetNames<PerformerCommandKind>(), Does.Not.Contain("SinkParamToAsset"));

            string repoRoot = FindRepoRoot();
            string architecture = File.ReadAllText(Path.Combine(
                repoRoot,
                "gitbook",
                "architecture",
                "performer-as-actor-architecture.md"));
            string prd = File.ReadAllText(Path.Combine(
                repoRoot,
                "docs",
                "prd",
                "17-performer-authoring-runtime.html"));
            string report = File.ReadAllText(Path.Combine(
                repoRoot,
                "artifacts",
                "performer-authoring-runtime-pipeline.html"));
            string coreRoot = Path.Combine(repoRoot, "src", "Core");
            string[] retiredPresentationBehaviorReferences = Directory
                .GetFiles(coreRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("PresentationBehavior", StringComparison.Ordinal))
                .ToArray();
            string configCatalog = File.ReadAllText(Path.Combine(
                repoRoot,
                "assets",
                "Configs",
                "config_catalog.json"));

            Assert.Multiple(() =>
            {
                Assert.That(architecture, Does.Contain("`ParamTween` 是 `BehaviorKind` 的一种"));
                Assert.That(prd, Does.Contain("Rule -> Command -> Runtime State -> Behavior"));
                Assert.That(report, Does.Contain("Rule → Command → Runtime State → Behavior"));
                Assert.That(prd, Does.Not.Contain("Behavior 决定何时发生"));
                Assert.That(prd, Does.Not.Contain("执行 Behavior、Command、Tween"));
                Assert.That(report, Does.Not.Contain("Behavior 触发，Command 管创建与停止"));
                Assert.That(report, Does.Not.Contain("Behavior、Command、Tween 和语义组合"));
                Assert.That(retiredPresentationBehaviorReferences, Is.Empty);
                Assert.That(configCatalog, Does.Not.Contain("Presentation/presentation_behaviors.json"));
                AssertGapIsOpen(architecture, "PG-004");
                AssertGapIsOpen(architecture, "PG-005");
                AssertGapIsClosed(architecture, "PG-006");
            });
        }

        private static void AssertGapIsOpen(string architecture, string gapId)
        {
            string line = architecture.Split('\n').Single(value => value.Contains($"| {gapId} |", StringComparison.Ordinal));
            Assert.That(line, Does.Contain("未对齐"), $"{gapId} must remain explicit until its implementation debt is removed.");
        }

        private static void AssertGapIsClosed(string architecture, string gapId)
        {
            string line = architecture.Split('\n').Single(value => value.Contains($"| {gapId} |", StringComparison.Ordinal));
            Assert.That(line, Does.Contain("已对齐"), $"{gapId} must stay closed after its implementation debt is removed.");
        }

        private static JsonObject ReadObject(string path)
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidDataException($"Expected JSON object at '{path}'.");
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj.");
        }
    }
}
