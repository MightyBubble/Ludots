using System;
using System.Collections.Generic;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class ProviderContractTests
    {
        [Test]
        public void RegisterTryGetMustGet_RoundTrip()
        {
            var services = CreateServices(allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);

            Assert.That(services.Sources.TryGet("fixture.signal_ping").Found, Is.True);
            ISourceProvider source = services.Sources.MustGet("fixture.signal_ping", out ProviderParameterSchema schema);
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
            var services = CreateServices(allowTestDomainOverride: false);
            FixtureProviderInstaller.InstallMinimal(services);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                services.Effects.Register("fixture.noop", new FixtureEffectHandler(), ProviderParameterSchema.Empty));
            Assert.That(ex!.Message, Does.Contain(ProviderFailureCodes.DuplicateProviderKey));
            Assert.That(ex.Message, Does.Contain("fixture.noop"));
        }

        [Test]
        public void TestDomainOverride_AllowsFixtureReregistration()
        {
            var services = CreateServices(allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);

            Assert.DoesNotThrow(() =>
                services.Effects.Register("fixture.noop", new FixtureEffectHandler(), ProviderParameterSchema.Empty));
        }

        [Test]
        public void FrameworkGap_MustGetFailsWithNeedsProviderRegistration()
        {
            var services = CreateServices(allowTestDomainOverride: false);

            ProviderLookupResult<IEffectHandler> create = services.Effects.TryGet("task.create");
            Assert.That(create.Found, Is.False);
            Assert.That(create.FailureCode, Is.EqualTo(ProviderFailureCodes.NeedsProviderRegistration));
            Assert.That(create.Reason, Does.Contain("task.create"));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                services.Sources.MustGet("task.state_changed", out _));
            Assert.That(ex!.Message, Does.Contain(ProviderFailureCodes.NeedsProviderRegistration));
            Assert.That(ex.Message, Does.Contain("task.state_changed"));
        }

        [Test]
        public void UnregisteredDomainKey_FailsAsUnknown_NoStubs()
        {
            var services = CreateServices(allowTestDomainOverride: false);

            ProviderLookupResult<ISourceProvider> lookup = services.Sources.TryGet("supply.network_changed");
            Assert.That(lookup.Found, Is.False);
            Assert.That(lookup.FailureCode, Is.EqualTo(ProviderFailureCodes.UnknownProviderKey));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                services.Sources.MustGet("time.day_started", out _));
            Assert.That(ex!.Message, Does.Contain(ProviderFailureCodes.UnknownProviderKey));
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
        public void DefinitionValidator_RejectsGapAndUnknownKeys()
        {
            var services = CreateServices(allowTestDomainOverride: true);
            FixtureProviderInstaller.InstallMinimal(services);

            string json = """
            {
              "id": "activity.bad",
              "source_key": "fixture.signal_ping",
              "options": [
                { "effect_key": "fixture.noop" },
                { "effect_key": "task.create" },
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
                i.Key == "task.create"));
            Assert.That(report.Issues, Has.Some.Matches<ProviderValidationIssue>(i =>
                i.FailureCode == ProviderFailureCodes.UnknownProviderKey &&
                i.Key == "world.not_registered_yet"));

            Assert.Throws<InvalidOperationException>(() => services.Validator.ValidateAndThrow(refs));
        }

        [Test]
        public void ConditionWriteGuard_DetectsWorldMutation()
        {
            using World world = World.Create();
            var context = new ProviderExecutionContext(
                world,
                world.Create(),
                ProviderContextBinding.CreateBindings());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                ConditionWriteGuard.EvaluateReadOnly(
                    new WritingConditionProbe(),
                    context,
                    new Dictionary<string, object?>()));
            Assert.That(ex!.Message, Does.Contain(ProviderFailureCodes.ConditionWriteDetected));
        }

        [Test]
        public void GasOperatorWhitelist_AcceptsKnownKinds_RejectsUnknownWithName()
        {
            foreach (string kind in GasOperatorWhitelist.ExecKinds)
            {
                Assert.DoesNotThrow(() => GasOperatorWhitelist.ValidateExecItemKind(kind, "ability.test"));
            }

            foreach (string preset in GasOperatorWhitelist.PresetTypes)
            {
                Assert.DoesNotThrow(() => GasOperatorWhitelist.ValidateEffectPresetType(preset, "effect.test"));
            }

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                GasOperatorWhitelist.ValidateExecItemKind("TotallyFakeOp", "ability.test"));
            Assert.That(ex!.Message, Does.Contain("TotallyFakeOp"));
            Assert.That(ex.Message, Does.Contain("ability.test"));

            Assert.Throws<InvalidOperationException>(() =>
                GasOperatorWhitelist.ValidateEffectPresetType("TotallyFakePreset", "effect.test"));
            Assert.DoesNotThrow(() => GasOperatorWhitelist.ValidateEffectPresetType(string.Empty, "effect.test"));
        }

        private static ProviderServices CreateServices(bool allowTestDomainOverride) =>
            new(allowTestDomainOverride: allowTestDomainOverride);
    }
}
