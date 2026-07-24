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
    public void QueryFilter_SensorFlagIsRetainedAndStaleIgnoredBodyFailsExplicitly()
    {
        var sensorFilter = new Physics3DQueryFilter(LayerMask.All, ignoredBody: default, includeSensors: true);
        Assert.That(sensorFilter.IncludeSensors, Is.True);

        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(mobileCapacity: 1, staticCapacity: 1));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId stale = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(100f, 0f, 0f)));
        world.DestroyBody(stale);

        var filter = new Physics3DQueryFilter(LayerMask.All, stale);
        Assert.Throws<InvalidOperationException>(() =>
            world.RaycastAny(Vector3.Zero, Vector3.UnitX, 500f, filter));
    }

    [Test]
    public void Raycast_AllClosestAndAnyRespectLayerAndIgnoredBody()
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
        LayerMask excluded = new(category: 1u << 2, mask: uint.MaxValue);
        world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(200f, 0f, 0f),
            excluded));

        LayerMask included = new(category: 1u, mask: 1u);
        var filter = new Physics3DQueryFilter(included, near);
        Span<Physics3DRaycastHit> hits = stackalloc Physics3DRaycastHit[2];
        int count = world.Raycast(Vector3.Zero, Vector3.UnitX, 500f, filter, hits);

        Assert.That(count, Is.EqualTo(1));
        Assert.That(hits[0].Body, Is.EqualTo(far));
        Assert.That(world.RaycastClosest(Vector3.Zero, Vector3.UnitX, 500f, filter, out Physics3DRaycastHit closest), Is.True);
        Assert.That(closest.Body, Is.EqualTo(far));
        Assert.That(world.RaycastAny(Vector3.Zero, Vector3.UnitX, 500f, filter), Is.True);
        Assert.That(world.RaycastAny(Vector3.Zero, -Vector3.UnitX, 500f, filter), Is.False);

        var includeAll = new Physics3DQueryFilter(LayerMask.All);
        Assert.That(world.RaycastClosest(
            new Vector3(100f, 0f, 0f),
            Vector3.UnitX,
            500f,
            includeAll,
            out Physics3DRaycastHit startedInside), Is.True);
        Assert.That(startedInside.Body, Is.EqualTo(near));

        var tooSmall = new Physics3DRaycastHit[1];
        Assert.Throws<Physics3DCapacityExceededException>(() =>
            world.Raycast(Vector3.Zero, Vector3.UnitX, 500f, included, tooSmall));
    }

    [Test]
    public void ShapeCasts_AllClosestAndAnyReportInitialOverlapAndIgnoreSelf()
    {
        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(mobileCapacity: 1, staticCapacity: 2));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        Physics3DBodyId overlapped = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(100f, 0f, 0f)));
        Physics3DBodyId far = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(300f, 0f, 0f)));
        var all = new Physics3DQueryFilter(LayerMask.All);
        var ignoreOverlapped = new Physics3DQueryFilter(LayerMask.All, overlapped);

        Span<Physics3DShapeCastHit> hits = stackalloc Physics3DShapeCastHit[2];
        int boxCount = world.BoxCast(
            new Vector3(100f, 0f, 0f),
            new Vector3(10f),
            Quaternion.Identity,
            Vector3.UnitX,
            500f,
            all,
            hits);
        Assert.That(boxCount, Is.EqualTo(2));
        Assert.That(hits[0].Body, Is.EqualTo(overlapped));
        Assert.That(hits[0].StartedOverlapping, Is.True);

        Assert.That(world.BoxCastClosest(
            new Vector3(100f, 0f, 0f),
            new Vector3(10f),
            Quaternion.Identity,
            Vector3.UnitX,
            500f,
            ignoreOverlapped,
            out Physics3DShapeCastHit boxClosest), Is.True);
        Assert.That(boxClosest.Body, Is.EqualTo(far));
        Assert.That(boxClosest.StartedOverlapping, Is.False);

        Assert.That(world.SphereCastAny(
            new Vector3(100f, 0f, 0f),
            10f,
            Vector3.UnitX,
            50f,
            all), Is.True);
        Assert.That(world.SphereCastClosest(
            Vector3.Zero,
            5f,
            Vector3.UnitX,
            150f,
            all,
            out Physics3DShapeCastHit sphereClosest), Is.True);
        Assert.That(sphereClosest.Body, Is.EqualTo(overlapped));

        Assert.That(world.CapsuleCastAny(
            Vector3.Zero,
            5f,
            20f,
            Quaternion.Identity,
            Vector3.UnitX,
            150f,
            all), Is.True);
        Assert.That(world.CapsuleCastClosest(
            Vector3.Zero,
            5f,
            20f,
            Quaternion.Identity,
            Vector3.UnitX,
            150f,
            all,
            out Physics3DShapeCastHit capsuleClosest), Is.True);
        Assert.That(capsuleClosest.Body, Is.EqualTo(overlapped));
    }

    [Test]
    public void Overlap_IsUnorderedAppendOnlyAndNeverSilentlyTruncates()
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
        int filteredCount = world.OverlapSphere(new Vector3(15f, 0f, 0f), 40f, ignoreFirst, hits);
        Assert.That(filteredCount, Is.EqualTo(3));
        for (int i = 0; i < filteredCount; i++)
        {
            Assert.That(hits[i].Body, Is.Not.EqualTo(bodies[0]));
        }

        var tooSmall = new Physics3DOverlapHit[2];
        Assert.Throws<Physics3DCapacityExceededException>(() => world.OverlapCapsule(
            new Vector3(15f, 0f, 0f),
            20f,
            60f,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f),
            LayerMask.All,
            tooSmall));
    }

    [Test]
    public void RaycastClosestBatch_UsesPerRequestIgnoreSelfAndPrevalidatesWholeBatch()
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
        Physics3DBodyId above = world.CreateBody(Physics3DWorldTests.CreateBody(
            Physics3DBodyKind.Static,
            box,
            new Vector3(0f, 300f, 0f)));

        var requests = new Physics3DRaycastQuery[]
        {
            new(Vector3.Zero, Vector3.UnitX, 500f, new Physics3DQueryFilter(LayerMask.All, near)),
            new(Vector3.Zero, Vector3.UnitY, 500f, new Physics3DQueryFilter(LayerMask.All, above)),
            new(Vector3.Zero, -Vector3.UnitX, 500f, new Physics3DQueryFilter(LayerMask.All))
        };
        var results = new Physics3DBatchedRaycastClosestResult[requests.Length];

        world.RaycastClosestBatch(requests, results);
        Assert.That(results[0].Hit, Is.True);
        Assert.That(results[0].Value.Body, Is.EqualTo(far));
        Assert.That(results[1].Hit, Is.False);
        Assert.That(results[2].Hit, Is.False);

        Physics3DBatchedRaycastClosestResult[] beforeInvalidBatch = (Physics3DBatchedRaycastClosestResult[])results.Clone();
        var invalid = new Physics3DRaycastQuery[]
        {
            requests[0],
            new(Vector3.Zero, Vector3.Zero, 500f, new Physics3DQueryFilter(LayerMask.All)),
            requests[2]
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => world.RaycastClosestBatch(invalid, results));
        Assert.That(results, Is.EqualTo(beforeInvalidBatch), "Validation failure must leave every prior result untouched.");

        var mismatched = new Physics3DBatchedRaycastClosestResult[2];
        Assert.Throws<ArgumentException>(() => world.RaycastClosestBatch(requests, mismatched));
    }

    [Test]
    public void WarmedRaycastClosestBatch_HasZeroManagedAllocationsOnCallingThread()
    {
        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(mobileCapacity: 1, staticCapacity: 4, workerCount: 1));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        var bodies = new Physics3DBodyId[4];
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i] = world.CreateBody(Physics3DWorldTests.CreateBody(
                Physics3DBodyKind.Static,
                box,
                new Vector3(100f + i * 100f, 0f, 0f)));
        }

        var requests = new Physics3DRaycastQuery[4];
        var results = new Physics3DBatchedRaycastClosestResult[requests.Length];
        for (int i = 0; i < requests.Length; i++)
        {
            requests[i] = new Physics3DRaycastQuery(
                Vector3.Zero,
                Vector3.UnitX,
                500f,
                new Physics3DQueryFilter(LayerMask.All, bodies[i]));
        }

        for (int i = 0; i < 64; i++)
        {
            world.RaycastClosestBatch(requests, results);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            world.RaycastClosestBatch(requests, results);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Warmed ray batch allocated {allocated} managed bytes on the calling thread.");
    }
}
