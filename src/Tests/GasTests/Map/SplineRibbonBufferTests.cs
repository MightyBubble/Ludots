using System;
using System.Numerics;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class SplineRibbonBufferTests
    {
        [Test]
        public void SplineRibbonBuffer_WhenCapacityIsNotPositive_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SplineRibbonBuffer(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SplineRibbonBuffer(-1));
        }

        [Test]
        public void SplineRibbonBuffer_StoresColumns_WithoutItemMaterialization()
        {
            var buffer = new SplineRibbonBuffer(capacity: 2);

            Assert.That(buffer.TryAddLine(
                stableId: 11,
                start: new Vector3(1f, 0.1f, 2f),
                end: new Vector3(5f, 0.1f, 8f),
                width: 0.6f,
                fillColor: new Vector4(0.2f, 0.3f, 0.4f, 0.5f),
                borderColor: new Vector4(0.6f, 0.7f, 0.8f, 0.9f),
                borderWidth: 0.05f), Is.True);

            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.StableIds[0], Is.EqualTo(11));
            Assert.That(buffer.P0X[0], Is.EqualTo(1f));
            Assert.That(buffer.P3Z[0], Is.EqualTo(8f));
            Assert.That(buffer.Width[0], Is.EqualTo(0.6f));
            Assert.That(buffer.FillA[0], Is.EqualTo(0.5f));
            Assert.That(buffer.BorderWidth[0], Is.EqualTo(0.05f));
        }
    }
}
