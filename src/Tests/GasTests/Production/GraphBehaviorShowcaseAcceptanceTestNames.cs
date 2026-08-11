using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    // Named fixtures referenced by showcase.registry.json acceptanceTest fields.
    // Behavior is owned by GraphBehaviorSeparatedShowcaseAcceptanceTests (ci-gate).

    [TestFixture]
    [Category("ci-gate")]
    public sealed class BehaviorTreeArenaShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            new GraphBehaviorSeparatedShowcaseAcceptanceTests()
                .BehaviorTreeArena_PatrolVignette_ThinkWavesUnderBudget();
        }
    }

    [TestFixture]
    [Category("ci-gate")]
    public sealed class HfsmSentryArenaShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            new GraphBehaviorSeparatedShowcaseAcceptanceTests()
                .HfsmSentryArena_GateVignette_ThinkWavesUnderBudget();
        }
    }

    [TestFixture]
    [Category("ci-gate")]
    public sealed class LevelBlueprintTrialShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            new GraphBehaviorSeparatedShowcaseAcceptanceTests()
                .LevelBlueprintTrial_SpawnClearGate_AdvancesPhaseUnderBudget();
        }
    }

    [TestFixture]
    [Category("ci-gate")]
    public sealed class AbilityGraphSandboxShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            new GraphBehaviorSeparatedShowcaseAcceptanceTests()
                .AbilityGraphSandbox_CastArc_UnderBudget();
        }
    }

    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphBehaviorIntegrationShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            new GraphBehaviorSeparatedShowcaseAcceptanceTests()
                .GraphBehaviorIntegration_ShortPlay_UnderBudget();
        }
    }
}
