using System;
using System.Linq;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Regression lock: TagDisplay specialty lookup path must stay removed.
    /// </summary>
    [TestFixture]
    public sealed class TagDisplayGraphOpsTests
    {
        [Test]
        public void GraphNodeOp_DoesNotDefineLookupTagDisplayOrSelectTagInMask()
        {
            var names = Enum.GetNames(typeof(GraphNodeOp));
            Assert.That(names, Does.Not.Contain("LookupTagDisplayToken"));
            Assert.That(names, Does.Not.Contain("SelectTagInMask"));
        }

        [Test]
        public void GraphNodeOpParser_RejectsTagDisplayAuthoringSugar()
        {
            Assert.That(GraphNodeOpParser.TryParse("LookupTagDisplayText", out _), Is.False);
            Assert.That(GraphNodeOpParser.TryParse("ReadGameplayTag", out _), Is.False);
        }

        [Test]
        public void CoreServiceKeys_DoesNotExposeTagDisplayTableRegistry()
        {
            var fields = typeof(CoreServiceKeys).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.That(
                fields.Any(f => f.Name == "TagDisplayTableRegistry"),
                Is.False,
                "TagDisplayTableRegistry must not remain a production service key.");
        }

        [Test]
        public void TagDisplayTableRegistry_TypeIsRemoved()
        {
            var type = Type.GetType(
                "Ludots.Core.Presentation.TagDisplay.TagDisplayTableRegistry, Ludots.Core");
            Assert.That(type, Is.Null);
        }
    }
}
