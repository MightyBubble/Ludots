using System.IO;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class InputAutomationArchitectureTests
    {
        [Test]
        public void CoreOwnsHostNeutralInputAutomationContract()
        {
            string repoRoot = FindRepoRoot();
            string automationDir = Path.Combine(repoRoot, "src", "Core", "Input", "Automation");

            Assert.That(File.Exists(Path.Combine(automationDir, "InputAutomationTypes.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(automationDir, "InputAutomationPlayer.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(automationDir, "InputAutomationBackend.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(automationDir, "InputAutomationScriptLoader.cs")), Is.True);
        }

        [Test]
        public void RaylibAndWebUseSameInputAutomationScriptEntry()
        {
            string repoRoot = FindRepoRoot();
            string raylibComposer = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Adapters",
                "Raylib",
                "Ludots.Adapter.Raylib",
                "RaylibHostComposer.cs"));
            string webComposer = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Adapters",
                "Web",
                "Ludots.Adapter.Web",
                "WebHostComposer.cs"));

            Assert.That(raylibComposer, Does.Contain("InputAutomationScriptLoader.TryCreatePlayerFromEnvironment"));
            Assert.That(webComposer, Does.Contain("InputAutomationScriptLoader.TryCreatePlayerFromEnvironment"));
            Assert.That(raylibComposer, Does.Contain("InputAutomationBackend"));
            Assert.That(webComposer, Does.Contain("InputAutomationBackend"));
        }

        [Test]
        public void ArchitectureDocsDeclareInputAutomationAsHostNeutral()
        {
            string repoRoot = FindRepoRoot();
            string doc = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "architecture", "input-automation.md"));

            Assert.That(doc, Does.Contain("does not define screenshot or video capture"));
            Assert.That(doc, Does.Contain("LUDOTS_INPUT_AUTOMATION_SCRIPT"));
            Assert.That(doc, Does.Contain("Future hosts such as UE"));
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (current != null && !Directory.Exists(Path.Combine(current.FullName, "assets")))
            {
                current = current.Parent;
            }

            return current?.FullName ?? TestContext.CurrentContext.TestDirectory;
        }
    }
}
