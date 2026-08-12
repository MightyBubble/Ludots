using Ludots.Core.Gameplay.GAS.Capacity;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Binds a legacy-sized world column store for PresentationTests so CreateAttached works.
    /// </summary>
    [SetUpFixture]
    public sealed class GasCapacityPresentationTestSessionBootstrap
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            GasLoadTimeCapacitySession.ClearForTests();
            GasLoadTimeCapacitySession.EnsureLegacyPlanAndStoreForTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            GasLoadTimeCapacitySession.ClearForTests();
        }
    }
}
