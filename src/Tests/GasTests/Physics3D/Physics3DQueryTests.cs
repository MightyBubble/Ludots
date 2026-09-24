using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DQueryTests
{
    [Test]
    public void QueryFilter_IncludeSensors_IsExplicitlyUnsupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            _ = new Physics3DQueryFilter(LayerMask.All, ignoredBody: default, includeSensors: true));
    }

    [Test]
    public void QueryFilter_StaleIgnoredBody_FailsAtQueryTime()
    {
        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(mobileCapacity: 1, staticCapacity: 1));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId body = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(100f, 0f, 0f)));
        world.DestroyBody(body);

        var filter = new Physics3DQueryFilter(LayerMask.All, body);
        Assert.Throws<InvalidOperationException>(() =>
            world.RaycastAny(Vector3.Zero, Vector3.UnitX, 500f, filter));
    }

    [Test]
    public void Raycast_IgnoreSelf_ExcludesIgnoredBodyFromHits()
    {
        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(mobileCapacity: 1, staticCapacity: 2));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId near = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(100f, 0f, 0f)));
        Physics3DBodyId far = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(300f, 0f, 0f)));

        var ignoreNear = new Physics3DQueryFilter(LayerMask.All, near);
        Span<Physics3DRaycastHit> hits = stackalloc Physics3DRaycastHit[4];
        int count = world.Raycast(Vector3.Zero, Vector3.UnitX, 500f, ignoreNear, hits);

        Assert.That(count, Is.EqualTo(1));
        Assert.That(hits[0].Body, Is.EqualTo(far));
        Assert.That(world.RaycastAny(Vector3.Zero, Vector3.UnitX, 500f, ignoreNear), Is.True);
        Assert.That(world.RaycastClosest(Vector3.Zero, Vector3.UnitX, 500f, ignoreNear, out Physics3DRaycastHit closest), Is.True);
        Assert.That(closest.Body, Is.EqualTo(far));
    }

    [Test]
    public void RaycastClosestAndAny_SelectNearestOrExistenceWithoutCollectingAll()
    {
        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(mobileCapacity: 1, staticCapacity: 3));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId near = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(100f, 0f, 0f)));
        world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(200f, 0f, 0f)));
        world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(300f, 0f, 0f)));

        var filter = new Physics3DQueryFilter(LayerMask.All);
        Assert.That(world.RaycastClosest(Vector3.Zero, Vector3.UnitX, 500f, filter, out Physics3DRaycastHit closest), Is.True);
        Assert.That(closest.Body, Is.EqualTo(near));
        Assert.That(closest.DistanceCm, Is.EqualTo(90f).Within(0.1f));
        Assert.That(world.RaycastAny(Vector3.Zero, Vector3.UnitX, 500f, filter), Is.True);
        Assert.That(world.RaycastAny(Vector3.Zero, Vector3.UnitX, 50f, filter), Is.False);
        Assert.That(world.RaycastClosest(Vector3.Zero, Vector3.UnitX, 50f, filter, out _), Is.False);
    }

    [Test]
    public void ShapeCastClosest_ReportsStartedOverlapAsClosestHit()
    {
        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(mobileCapacity: 1, staticCapacity: 2));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId overlapped = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(100f, 0f, 0f)));
        world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(300f, 0f, 0f)));

        var filter = new Physics3DQueryFilter(LayerMask.All);
        Assert.That(
            world.BoxCastClosest(
                new Vector3(100f, 0f, 0f),
                new Vector3(10f),
                Quaternion.Identity,
                Vector3.UnitX,
                maximumDistanceCm: 500f,
                filter,
                out Physics3DShapeCastHit hit),
            Is.True);
        Assert.That(hit.Body, Is.EqualTo(overlapped));
        Assert.That(hit.DistanceCm, Is.Zero);
        Assert.That(hit.StartedOverlapping, Is.True);
        Assert.That(
            world.SphereCastAny(new Vector3(100f, 0f, 0f), radiusCm: 10f, Vector3.UnitX, 50f, filter),
            Is.True);
        Assert.That(
            world.CapsuleCastClosest(
                Vector3.Zero,
                radiusCm: 5f,
                cylinderLengthCm: 20f,
                Quaternion.Identity,
                Vector3.UnitX,
                maximumDistanceCm: 150f,
                filter,
                out Physics3DShapeCastHit capsuleHit),
            Is.True);
        Assert.That(capsuleHit.Body, Is.EqualTo(overlapped));
        Assert.That(capsuleHit.StartedOverlapping, Is.False);
    }

    [Test]
    public void Overlap_IsUnorderedAppendOnly_AndOverflowThrows()
    {
        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(mobileCapacity: 1, staticCapacity: 4));
        Physics3DShapeId sphere = world.RegisterSphereShape(5f);
        var bodies = new Physics3DBodyId[4];
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i] = world.CreateBody(Physics3DWorldTests.CreateBody(
                Physics3DBodyKind.Static,
                sphere,
                new Vector3(i * 10f, 0f, 0f)));
        }

        Span<Physics3DOverlapHit> hits = stackalloc Physics3DOverlapHit[4];
        int count = world.OverlapBox(
            new Vector3(15f, 0f, 0f),
            new Vector3(80f, 20f, 20f),
            Quaternion.Identity,
            new Physics3DQueryFilter(LayerMask.All),
            hits);
        Assert.That(count, Is.EqualTo(4));
        var seen = new HashSet<Physics3DBodyId>();
        for (int i = 0; i < count; i++)
        {
            Assert.That(seen.Add(hits[i].Body), Is.True);
        }

        foreach (Physics3DBodyId body in bodies)
        {
            Assert.That(seen.Contains(body), Is.True);
        }

        var ignoreFirst = new Physics3DQueryFilter(LayerMask.All, bodies[0]);
        int filtered = world.OverlapSphere(new Vector3(15f, 0f, 0f), radiusCm: 40f, ignoreFirst, hits);
        Assert.That(filtered, Is.EqualTo(3));
        for (int i = 0; i < filtered; i++)
        {
            Assert.That(hits[i].Body, Is.Not.EqualTo(bodies[0]));
        }

        var tooSmall = new Physics3DOverlapHit[2];
        Assert.Throws<Physics3DCapacityExceededException>(() => world.OverlapBox(
            new Vector3(15f, 0f, 0f),
            new Vector3(80f, 20f, 20f),
            Quaternion.Identity,
            LayerMask.All,
            tooSmall));
        Assert.Throws<Physics3DCapacityExceededException>(() => world.OverlapSphere(
            new Vector3(15f, 0f, 0f),
            40f,
            LayerMask.All,
            Span<Physics3DOverlapHit>.Empty));
    }

    [Test]
    public void RaycastAll_OrdersByDistanceThenSlot_AndOverflowThrows()
    {
        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(mobileCapacity: 1, staticCapacity: 3));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId first = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(100f, 0f, 0f)));
        Physics3DBodyId second = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(200f, 0f, 0f)));
        Physics3DBodyId third = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(300f, 0f, 0f)));

        Span<Physics3DRaycastHit> hits = stackalloc Physics3DRaycastHit[3];
        int count = world.Raycast(Vector3.Zero, Vector3.UnitX, 500f, LayerMask.All, hits);
        Assert.That(count, Is.EqualTo(3));
        Assert.That(hits[0].Body, Is.EqualTo(first));
        Assert.That(hits[1].Body, Is.EqualTo(second));
        Assert.That(hits[2].Body, Is.EqualTo(third));
        Assert.That(hits[0].DistanceCm, Is.LessThan(hits[1].DistanceCm));
        Assert.That(hits[1].DistanceCm, Is.LessThan(hits[2].DistanceCm));

        var tooSmall = new Physics3DRaycastHit[1];
        Assert.Throws<Physics3DCapacityExceededException>(() =>
            world.Raycast(Vector3.Zero, Vector3.UnitX, 500f, LayerMask.All, tooSmall));
    }

    [Test]
    public void RaycastClosestBatch_AlignsOneToOneAndRespectsIgnoreFilter()
    {
        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(mobileCapacity: 1, staticCapacity: 3));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId near = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(100f, 0f, 0f)));
        Physics3DBodyId far = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(300f, 0f, 0f)));
        world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(0f, 300f, 0f)));

        var requests = new Physics3DRaycastQuery[]
        {
            new(Vector3.Zero, Vector3.UnitX, 500f),
            new(Vector3.Zero, Vector3.UnitY, 500f),
            new(Vector3.Zero, -Vector3.UnitX, 500f)
        };
        var results = new Physics3DBatchedRaycastClosestResult[3];

        world.RaycastClosestBatch(requests, new Physics3DQueryFilter(LayerMask.All), results);
        Assert.That(results[0].Hit, Is.True);
        Assert.That(results[0].Value.Body, Is.EqualTo(near));
        Assert.That(results[1].Hit, Is.True);
        Assert.That(results[2].Hit, Is.False);

        world.RaycastClosestBatch(requests, new Physics3DQueryFilter(LayerMask.All, near), results);
        Assert.That(results[0].Hit, Is.True);
        Assert.That(results[0].Value.Body, Is.EqualTo(far));

        var mismatched = new Physics3DBatchedRaycastClosestResult[2];
        Assert.Throws<ArgumentException>(() =>
            world.RaycastClosestBatch(requests, new Physics3DQueryFilter(LayerMask.All), mismatched));
    }

    [Test]
    public void WarmedFilteredQueries_HaveZeroManagedAllocationsOnCallingThread()
    {
        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(mobileCapacity: 1, staticCapacity: 4, workerCount: 1));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId ignored = default;
        for (int i = 0; i < 4; i++)
        {
            Physics3DBodyId body = world.CreateBody(Physics3DWorldTests.CreateBody(
                Physics3DBodyKind.Static,
                box,
                new Vector3(100f + i * 100f, 0f, 0f)));
            if (i == 0)
            {
                ignored = body;
            }
        }

        var filter = new Physics3DQueryFilter(LayerMask.All, ignored);
        Span<Physics3DRaycastHit> rayHits = stackalloc Physics3DRaycastHit[4];
        Span<Physics3DShapeCastHit> castHits = stackalloc Physics3DShapeCastHit[4];
        Span<Physics3DOverlapHit> overlapHits = stackalloc Physics3DOverlapHit[4];
        for (int i = 0; i < 64; i++)
        {
            world.Raycast(Vector3.Zero, Vector3.UnitX, 500f, filter, rayHits);
            world.RaycastClosest(Vector3.Zero, Vector3.UnitX, 500f, filter, out _);
            world.RaycastAny(Vector3.Zero, Vector3.UnitX, 500f, filter);
            world.SphereCast(Vector3.Zero, 5f, Vector3.UnitX, 500f, filter, castHits);
            world.SphereCastClosest(Vector3.Zero, 5f, Vector3.UnitX, 500f, filter, out _);
            world.BoxCastAny(Vector3.Zero, new Vector3(10f), Quaternion.Identity, Vector3.UnitX, 500f, filter);
            world.OverlapBox(new Vector3(250f, 0f, 0f), new Vector3(500f, 30f, 30f), Quaternion.Identity, filter, overlapHits);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            world.Raycast(Vector3.Zero, Vector3.UnitX, 500f, filter, rayHits);
            world.RaycastClosest(Vector3.Zero, Vector3.UnitX, 500f, filter, out _);
            world.RaycastAny(Vector3.Zero, Vector3.UnitX, 500f, filter);
            world.SphereCast(Vector3.Zero, 5f, Vector3.UnitX, 500f, filter, castHits);
            world.SphereCastClosest(Vector3.Zero, 5f, Vector3.UnitX, 500f, filter, out _);
            world.BoxCastAny(Vector3.Zero, new Vector3(10f), Quaternion.Identity, Vector3.UnitX, 500f, filter);
            world.OverlapBox(new Vector3(250f, 0f, 0f), new Vector3(500f, 30f, 30f), Quaternion.Identity, filter, overlapHits);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Physics3D filtered queries allocated {allocated} managed bytes after warmup.");
    }
}
