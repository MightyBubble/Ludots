using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class SourceTextScannerTests
{
    [Test]
    public void ReadCodeLines_StripsStringTextButPreservesInterpolatedExpressions()
    {
        string file = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"SourceTextScannerTests_{Guid.NewGuid():N}.cs");
        File.WriteAllLines(file, new[]
        {
            "var hidden = \"source.GetRelationship<LegacyType>(target)\";",
            "var exposed = $\"{source.GetRelationship<LegacyType>(target)}\";",
            "var escaped = $\"{{source.GetRelationship<LegacyType>(target)}}\";",
        });

        try
        {
            var lines = SourceTextScanner.ReadCodeLines(file).ToArray();

            Assert.That(lines[0].Text, Does.Not.Contain("GetRelationship<LegacyType>"));
            Assert.That(lines[1].Text, Does.Contain("source.GetRelationship<LegacyType>(target)"));
            Assert.That(lines[2].Text, Does.Not.Contain("GetRelationship<LegacyType>"));
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
