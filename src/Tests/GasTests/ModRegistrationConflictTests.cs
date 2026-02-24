using System;
using System.IO;
using NUnit.Framework;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GasTests
{
    [TestFixture]
    public class ModRegistrationConflictTests
    {
        private RegistrationConflictReport _report;

        [SetUp]
        public void SetUp()
        {
            _report = new RegistrationConflictReport();
        }

        [Test]
        public void ConflictReport_RecordsEntry()
        {
            _report.Add("TestRegistry", "key1", "ModA", "ModB");

            Assert.That(_report.Count, Is.EqualTo(1));
            Assert.That(_report.Conflicts[0].RegistryName, Is.EqualTo("TestRegistry"));
            Assert.That(_report.Conflicts[0].Key, Is.EqualTo("key1"));
            Assert.That(_report.Conflicts[0].ExistingModId, Is.EqualTo("ModA"));
            Assert.That(_report.Conflicts[0].NewModId, Is.EqualTo("ModB"));
        }

        [Test]
        public void ConflictReport_PrintSummary_NoConflicts()
        {
            var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                _report.PrintSummary();
                var output = sw.ToString();
                Assert.That(output, Does.Contain("No registration conflicts detected"));
            }
            finally
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            }
        }

        [Test]
        public void ConflictReport_PrintSummary_WithConflicts()
        {
            _report.Add("ComponentRegistry", "Health", "CoreMod", "OverrideMod");
            _report.Add("FunctionRegistry", "DamageCalc", "CoreMod", "BalanceMod");

            var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                _report.PrintSummary();
                var output = sw.ToString();
                Assert.That(output, Does.Contain("2 registration conflict(s) detected"));
                Assert.That(output, Does.Contain("'Health' registered by 'CoreMod', overwritten by 'OverrideMod'"));
                Assert.That(output, Does.Contain("'DamageCalc' registered by 'CoreMod', overwritten by 'BalanceMod'"));
            }
            finally
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            }
        }

        [Test]
        public void FunctionRegistry_DuplicateRegister_RecordsConflict()
        {
            var fr = new FunctionRegistry();
            fr.SetConflictReport(_report);

            fr.Register("myFunc", ctx => null, "ModA");
            fr.Register("myFunc", ctx => 42, "ModB");

            // In DEBUG, conflict should be recorded
#if DEBUG
            Assert.That(_report.Count, Is.EqualTo(1));
            Assert.That(_report.Conflicts[0].ExistingModId, Is.EqualTo("ModA"));
            Assert.That(_report.Conflicts[0].NewModId, Is.EqualTo("ModB"));
#else
            Assert.Pass("Conflict detection only active in DEBUG builds.");
#endif
        }

        [Test]
        public void EffectTemplateRegistry_DuplicateRegister_RecordsConflict()
        {
            var etr = new EffectTemplateRegistry();
            etr.SetConflictReport(_report);

            var data = new EffectTemplateData { TagId = 1 };
            etr.Register(10, in data, "ModA");
            etr.Register(10, in data, "ModB");

#if DEBUG
            Assert.That(_report.Count, Is.EqualTo(1));
            Assert.That(_report.Conflicts[0].Key, Is.EqualTo("10"));
#else
            Assert.Pass("Conflict detection only active in DEBUG builds.");
#endif
        }

        [Test]
        public void AbilityDefinitionRegistry_DuplicateRegister_RecordsConflict()
        {
            var adr = new AbilityDefinitionRegistry();
            adr.SetConflictReport(_report);

            var def = new AbilityDefinition();
            adr.Register(5, in def, "ModA");
            adr.Register(5, in def, "ModB");

#if DEBUG
            Assert.That(_report.Count, Is.EqualTo(1));
            Assert.That(_report.Conflicts[0].Key, Is.EqualTo("5"));
#else
            Assert.Pass("Conflict detection only active in DEBUG builds.");
#endif
        }
    }
}
