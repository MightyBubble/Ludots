using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class Y5kProviderContractTests
    {
        private static readonly string ArtifactRoot = Path.Combine(
            FindRepoRoot(),
            "artifacts",
            "acceptance",
            "y5k",
            "provider_contract");

        [Test]
        public void RegisterTryGetMustGet_RoundTripWithoutEmptyShim()
        {
            var services = new ProviderServices(registerDefaultGaps: false, allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);

            Assert.That(services.Sources.TryGet("fixture.signal_ping").Found, Is.True);
            ProviderParameterSchema schema;
            ISourceProvider source = services.Sources.MustGet("fixture.signal_ping", out schema);
            Assert.That(source, Is.Not.Null);
            Assert.That(schema, Is.Not.Null);

            ProviderLookupResult<IEffectHandler> miss = services.Effects.TryGet("fixture.missing_key");
            Assert.That(miss.Found, Is.False);
            Assert.That(miss.Implementation, Is.Null);
            Assert.That(miss.FailureCode, Is.EqualTo(ProviderFailureCodes.UnknownProviderKey));
            Assert.That(miss.Reason, Does.Contain("fixture.missing_key"));
        }

        [Test]
        public void DuplicateRegistration_FailsFast()
        {
            var services = new ProviderServices(registerDefaultGaps: false, allowTestDomainOverride: false);
            FixtureProviderInstaller.InstallMinimal(services);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                services.Effects.Register("fixture.noop", new FixtureEffectHandler(), ProviderParameterSchema.Empty));
            Assert.That(ex!.Message, Does.Contain(ProviderFailureCodes.DuplicateProviderKey));
            Assert.That(ex.Message, Does.Contain("fixture.noop"));
        }

        [Test]
        public void GapEntry_MustGetFailsWithNeedsProviderRegistration()
        {
            var services = new ProviderServices(registerDefaultGaps: true, allowTestDomainOverride: false);

            ProviderLookupResult<IEffectHandler> lookup = services.Effects.TryGet("city_control.commit_troops_takeover");
            Assert.That(lookup.Found, Is.False);
            Assert.That(lookup.FailureCode, Is.EqualTo(ProviderFailureCodes.NeedsProviderRegistration));
            Assert.That(lookup.Reason, Does.Contain("city_control.commit_troops_takeover"));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                services.Effects.MustGet("population.appoint_governor", out _));
            Assert.That(ex!.Message, Does.Contain(ProviderFailureCodes.NeedsProviderRegistration));
            Assert.That(ex.Message, Does.Contain("population.appoint_governor"));
        }

        [Test]
        public void InvalidKeyFormAndUnknownDomain_FailAtParse()
        {
            Assert.That(ProviderKey.TryParse("NotAKey", out _, out string code1, out _), Is.False);
            Assert.That(code1, Is.EqualTo(ProviderFailureCodes.InvalidProviderKeyForm));

            Assert.That(ProviderKey.TryParse("y5k.fake_action", out _, out string code2, out string reason2), Is.False);
            Assert.That(code2, Is.EqualTo(ProviderFailureCodes.DomainNotAllowed));
            Assert.That(reason2, Does.Contain("y5k"));
        }

        [Test]
        public void ContextBinding_OnlyThreePrefixesAllowed()
        {
            Assert.DoesNotThrow(() => ProviderContextBinding.ValidateReference("literal.ok", "opt.a"));
            Assert.DoesNotThrow(() => ProviderContextBinding.ValidateReference("signal.ok", "opt.b"));
            Assert.DoesNotThrow(() => ProviderContextBinding.ValidateReference("context.ok", "opt.c"));
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                ProviderContextBinding.ValidateReference("runtime.eval", "opt.d"));
            Assert.That(ex!.Message, Does.Contain("literal.*"));
        }

        [Test]
        public void DefinitionValidator_RejectsUnknownAndGapKeys()
        {
            var services = new ProviderServices(registerDefaultGaps: true, allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);

            string json = """
            {
              "id": "activity.bad",
              "source_key": "fixture.signal_ping",
              "options": [
                { "effect_key": "fixture.noop" },
                { "effect_key": "city_control.commit_troops_takeover" },
                { "condition_key": "world.not_registered_yet" }
              ]
            }
            """;

            using JsonDocument doc = JsonDocument.Parse(json);
            IReadOnlyList<ProviderDefinitionReference> refs =
                ProviderDefinitionValidator.CollectFromJsonDocument("activity.bad", doc.RootElement);

            ProviderValidationReport report = services.Validator.Validate(refs);
            Assert.That(report.Passed, Is.False);
            Assert.That(report.Issues, Has.Some.Matches<ProviderValidationIssue>(i =>
                i.FailureCode == ProviderFailureCodes.NeedsProviderRegistration &&
                i.Key == "city_control.commit_troops_takeover"));
            Assert.That(report.Issues, Has.Some.Matches<ProviderValidationIssue>(i =>
                i.FailureCode == ProviderFailureCodes.UnknownProviderKey &&
                i.Key == "world.not_registered_yet"));

            WriteAcceptanceArtifacts(report);
        }

        [Test]
        public void ConditionWriteGuard_DetectsWorldMutation()
        {
            using World world = World.Create();
            Entity subject = world.Create();
            var context = new ProviderExecutionContext(
                world,
                subject,
                ProviderContextBinding.CreateBindings());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                ConditionWriteGuard.EvaluateReadOnly(
                    new WritingConditionProbe(),
                    context,
                    new Dictionary<string, object?>()));
            Assert.That(ex!.Message, Does.Contain(ProviderFailureCodes.ConditionWriteDetected));
        }

        [Test]
        public void ParameterSchema_RejectsUndeclaredField()
        {
            var schema = new ProviderParameterSchema(new[]
            {
                new ProviderParameterField("note", ProviderParameterKind.String, required: false),
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                schema.Validate(
                    new Dictionary<string, object?> { ["extra"] = "x" },
                    "effect.params"));
            Assert.That(ex!.Message, Does.Contain(ProviderFailureCodes.ParameterSchemaMismatch));
            Assert.That(ex.Message, Does.Contain("extra"));
        }

        private static void WriteAcceptanceArtifacts(ProviderValidationReport report)
        {
            Directory.CreateDirectory(ArtifactRoot);
            File.WriteAllText(
                Path.Combine(ArtifactRoot, "config-snapshot.json"),
                JsonSerializer.Serialize(new
                {
                    schema = "y5k_provider_contract_v1",
                    expected_load_result = "reject",
                    referenced_key_count = report.ReferencedKeys.Count,
                    issue_count = report.Issues.Count,
                }, new JsonSerializerOptions { WriteIndented = true }));

            var trace = new StringBuilder();
            foreach (ProviderDefinitionReference reference in report.ReferencedKeys)
            {
                trace.AppendLine(JsonSerializer.Serialize(new
                {
                    event_type = "provider_key_referenced",
                    definition_id = reference.DefinitionId,
                    field_path = reference.FieldPath,
                    kind = reference.Kind.ToString(),
                    key = reference.Key,
                }));
            }

            foreach (ProviderValidationIssue issue in report.Issues)
            {
                trace.AppendLine(JsonSerializer.Serialize(new
                {
                    event_type = "provider_validation_issue",
                    failure_code = issue.FailureCode,
                    key = issue.Key,
                    definition_id = issue.DefinitionId,
                    field_path = issue.FieldPath,
                    message = issue.Message,
                }));
            }

            File.WriteAllText(Path.Combine(ArtifactRoot, "trace.jsonl"), trace.ToString());
            File.WriteAllText(
                Path.Combine(ArtifactRoot, "presentation-requests.jsonl"),
                JsonSerializer.Serialize(new
                {
                    cue = "provider_load_rejected",
                    issue_count = report.Issues.Count,
                }) + Environment.NewLine);

            File.WriteAllText(
                Path.Combine(ArtifactRoot, "battle-report.md"),
                "# Provider contract acceptance\n\n" +
                $"- Referenced keys: {report.ReferencedKeys.Count}\n" +
                $"- Issues: {report.Issues.Count}\n" +
                "- Result: reject unknown/gap keys with named failure codes.\n");

            File.WriteAllText(
                Path.Combine(ArtifactRoot, "path.mmd"),
                "flowchart TD\n" +
                "  A[Collect keys] --> B[Validate registries]\n" +
                "  B -->|gap| C[needs_provider_registration]\n" +
                "  B -->|missing| D[unknown_provider_key]\n" +
                "  B -->|ok| E[Pass]\n");
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new InvalidOperationException("Unable to locate repository root from test directory.");
        }
    }
}
