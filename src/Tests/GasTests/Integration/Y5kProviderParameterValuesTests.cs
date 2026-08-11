using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Gameplay.Providers;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class Y5kProviderParameterValuesTests
    {
        [Test]
        public void NormalizeMap_CoercesJsonElementNumbersAndStrings()
        {
            using JsonDocument doc = JsonDocument.Parse("""{"settlement_key":3,"path":"garrison","amount":5.5,"flag":true}""");
            var raw = new Dictionary<string, object?>
            {
                ["settlement_key"] = doc.RootElement.GetProperty("settlement_key").Clone(),
                ["path"] = doc.RootElement.GetProperty("path").Clone(),
                ["amount"] = doc.RootElement.GetProperty("amount").Clone(),
                ["flag"] = doc.RootElement.GetProperty("flag").Clone(),
            };

            Dictionary<string, object?> normalized = ProviderParameterValues.NormalizeMap(raw);
            var schema = new ProviderParameterSchema(new[]
            {
                new ProviderParameterField("settlement_key", ProviderParameterKind.Int, required: true),
                new ProviderParameterField("path", ProviderParameterKind.String, required: true),
                new ProviderParameterField("amount", ProviderParameterKind.Float, required: true),
                new ProviderParameterField("flag", ProviderParameterKind.Bool, required: true),
            });
            Assert.DoesNotThrow(() => schema.Validate(normalized, "fixture"));
            Assert.That(ProviderParameterValues.ReadInt(normalized, "settlement_key"), Is.EqualTo(3));
            Assert.That(ProviderParameterValues.ReadString(normalized, "path"), Is.EqualTo("garrison"));
            Assert.That(ProviderParameterValues.ReadFloat(normalized, "amount"), Is.EqualTo(5.5f).Within(0.001f));
            Assert.That(ProviderParameterValues.ReadBool(normalized, "flag", defaultValue: false), Is.True);
        }
    }
}
