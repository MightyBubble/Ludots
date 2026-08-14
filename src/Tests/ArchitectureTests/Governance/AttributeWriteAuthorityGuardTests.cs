using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [Category("ci-gate")]
    [Category("arch-guard")]
    public sealed class AttributeWriteAuthorityGuardTests
    {
        [Test]
        public void AttributeBufferWrites_MustComeFromWhitelistedCallers()
        {
            new Governance.ArchitectureGuardTests().AttributeBufferWrites_MustComeFromWhitelistedCallers();
        }

        [Test]
        public void Guard_ScansShowcaseAssembliesNotOnlyCore()
        {
            Assembly[] assemblies = Governance.ArchitectureGuardTests.CollectAttributeBufferWriteScanAssemblies();
            string[] names = assemblies.Select(static assembly => assembly.GetName().Name ?? string.Empty).ToArray();

            Assert.That(names, Does.Contain("Ludots.Core"));
            Assert.That(names, Does.Contain("GoldMarketShowcaseMod"));
            Assert.That(names, Does.Contain("ItemSystemShowcaseMod"));
            Assert.That(names, Does.Contain("GenreInfoShowcaseMod"));
            Assert.That(names, Does.Contain("UiPlayerAggregateGraphMvpShowcaseMod"));
            Assert.That(names, Does.Contain("FourXAssociationShowcaseMod"));
            Assert.That(names, Does.Contain("CapabilityStandardLiveSkillWorkbenchShowcaseMod"));
            Assert.That(names, Does.Contain("CapabilityStandardGraphOpsQueryMod"));
            Assert.That(names, Does.Contain("CapabilityStandardGraphOpsAttrMod"));
            Assert.That(names, Does.Contain("PerformanceVisualizationMod"));
            Assert.That(names, Does.Contain("GasBenchmarkMod"));
            Assert.That(names.Count(static name => !string.Equals(name, "Ludots.Core", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(10));
        }
    }
}
