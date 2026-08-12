using Ludots.Core.Gameplay.GAS.Capacity;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Binds a legacy-sized world attribute column store for GasTests so CreateAttached works
    /// without each fixture repeating freeze/EnsureStore. Capacity plan tests rebind as needed.
    /// </summary>
    [SetUpFixture]
    public sealed class GasCapacityTestSessionBootstrap
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            GasLoadTimeCapacitySession.ClearForTests();
            GasLoadTimeCapacitySession.EnsureLegacyPlanAndStore();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            GasLoadTimeCapacitySession.ClearForTests();
        }
    }
}
