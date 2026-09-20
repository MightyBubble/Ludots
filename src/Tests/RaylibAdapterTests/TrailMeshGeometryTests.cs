using System;
using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    [TestFixture]
    public sealed class TrailMeshGeometryTests
    {
        private const float Epsilon = 0.0001f;

        [Test]
        public void WriteTrailStrip_SingleSample_WritesNothing()
        {
            TrailMeshSample[] samples =
            {
                new() { Base = Vector3.Zero, Tip = Vector3.UnitZ, Age01 = 0f },
            };
            Span<TrailMeshVertex> vertices = stackalloc TrailMeshVertex[TrailMeshGeometry.MaxStripVertices];

            Assert.That(
                TrailMeshGeometry.WriteTrailStrip(samples, Vector4.One, Vector4.Zero, vertices),
                Is.Zero);
        }

        [Test]
        public void WriteTrailStrip_TwoSamples_WeavesOneQuadAsTwoTriangles()
        {
            Vector3 b0 = new(0f, 1f, 0f);
            Vector3 t0 = new(0f, 1f, 1f);
            Vector3 b1 = new(1f, 1f, 0f);
            Vector3 t1 = new(1f, 1f, 1f);
            TrailMeshSample[] samples =
            {
                new() { Base = b0, Tip = t0, Age01 = 0f },
                new() { Base = b1, Tip = t1, Age01 = 1f },
            };
            Span<TrailMeshVertex> vertices = stackalloc TrailMeshVertex[TrailMeshGeometry.MaxStripVertices];

            int written = TrailMeshGeometry.WriteTrailStrip(samples, Vector4.One, Vector4.Zero, vertices);

            Assert.That(written, Is.EqualTo(6));
            AssertVec3(vertices[0].Position, b0);
            AssertVec3(vertices[1].Position, b1);
            AssertVec3(vertices[2].Position, t0);
            AssertVec3(vertices[3].Position, b1);
            AssertVec3(vertices[4].Position, t1);
            AssertVec3(vertices[5].Position, t0);
        }

        [Test]
        public void WriteTrailStrip_AgesAlongStrip_LerpHeadToTailColor()
        {
            var head = new Vector4(1f, 0.5f, 0.25f, 1f);
            var tail = new Vector4(0f, 0.1f, 0.5f, 0f);
            TrailMeshSample[] samples =
            {
                new() { Base = Vector3.Zero, Tip = Vector3.UnitZ, Age01 = 0f },
                new() { Base = Vector3.UnitX, Tip = Vector3.UnitX + Vector3.UnitZ, Age01 = 0.5f },
                new() { Base = Vector3.UnitX * 2f, Tip = (Vector3.UnitX * 2f) + Vector3.UnitZ, Age01 = 1f },
            };
            Span<TrailMeshVertex> vertices = stackalloc TrailMeshVertex[TrailMeshGeometry.MaxStripVertices];

            int written = TrailMeshGeometry.WriteTrailStrip(samples, in head, in tail, vertices);

            Assert.That(written, Is.EqualTo(12));
            AssertVec4(vertices[0].Color, head);
            AssertVec4(vertices[1].Color, Vector4.Lerp(head, tail, 0.5f));
            AssertVec4(vertices[3].Color, Vector4.Lerp(head, tail, 0.5f));
            AssertVec4(vertices[7].Color, tail);
        }

        [Test]
        public void WriteTrailStrip_MaxSamples_FillsDeclaredVertexBudget()
        {
            var samples = new TrailMeshSample[TrailMeshBuffer.MaxSamplesPerTrail];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = new TrailMeshSample
                {
                    Base = new Vector3(i, 0f, 0f),
                    Tip = new Vector3(i, 1f, 0f),
                    Age01 = i / (float)(samples.Length - 1),
                };
            }

            Span<TrailMeshVertex> vertices = stackalloc TrailMeshVertex[TrailMeshGeometry.MaxStripVertices];

            Assert.That(
                TrailMeshGeometry.WriteTrailStrip(samples, Vector4.One, Vector4.Zero, vertices),
                Is.EqualTo(TrailMeshGeometry.MaxStripVertices));
        }

        [Test]
        public void WriteTrailStrip_UndersizedSpan_Throws()
        {
            TrailMeshSample[] samples =
            {
                new() { Base = Vector3.Zero, Tip = Vector3.UnitZ, Age01 = 0f },
                new() { Base = Vector3.UnitX, Tip = Vector3.UnitX + Vector3.UnitZ, Age01 = 1f },
            };
            TrailMeshVertex[] vertices = new TrailMeshVertex[3];

            Assert.Throws<InvalidOperationException>(
                () => TrailMeshGeometry.WriteTrailStrip(samples, Vector4.One, Vector4.Zero, vertices));
        }

        private static void AssertVec3(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Epsilon));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Epsilon));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Epsilon));
        }

        private static void AssertVec4(Vector4 actual, Vector4 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Epsilon));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Epsilon));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Epsilon));
            Assert.That(actual.W, Is.EqualTo(expected.W).Within(Epsilon));
        }
    }
}
