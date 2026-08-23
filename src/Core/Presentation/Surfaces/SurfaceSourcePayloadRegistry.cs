using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Surfaces
{
    public readonly struct SurfaceSplineSegment
    {
        public SurfaceSplineSegment(in Vector3 p0, in Vector3 p1, in Vector3 p2, in Vector3 p3, float widthMeters)
        {
            P0 = p0;
            P1 = p1;
            P2 = p2;
            P3 = p3;
            WidthMeters = widthMeters;
        }

        public Vector3 P0 { get; }
        public Vector3 P1 { get; }
        public Vector3 P2 { get; }
        public Vector3 P3 { get; }
        public float WidthMeters { get; }
    }

    public readonly struct SurfaceSplineRibbonPayload
    {
        public SurfaceSplineRibbonPayload(SurfaceSplineSegment[] segments)
        {
            Segments = segments ?? throw new ArgumentNullException(nameof(segments));
        }

        public SurfaceSplineSegment[] Segments { get; }
    }

    public readonly struct SurfaceClosedAreaPayload
    {
        public SurfaceClosedAreaPayload(Vector3[] boundaryPoints)
        {
            BoundaryPoints = boundaryPoints ?? throw new ArgumentNullException(nameof(boundaryPoints));
        }

        public Vector3[] BoundaryPoints { get; }
    }

    public readonly struct SurfaceRawProceduralMeshPayload
    {
        public SurfaceRawProceduralMeshPayload(ProceduralMeshAssetData proceduralMesh, in Vector3 worldOrigin)
        {
            ProceduralMesh = proceduralMesh ?? throw new ArgumentNullException(nameof(proceduralMesh));
            WorldOrigin = worldOrigin;
        }

        public ProceduralMeshAssetData ProceduralMesh { get; }
        public Vector3 WorldOrigin { get; }
    }

    public readonly struct SurfacePayloadSnapshot
    {
        public SurfacePayloadSnapshot(
            PresenterSurfaceKind kind,
            int version,
            SurfaceSplineRibbonPayload splineRibbon,
            SurfaceClosedAreaPayload closedArea,
            SurfaceRawProceduralMeshPayload rawProceduralMesh)
        {
            Kind = kind;
            Version = version;
            SplineRibbon = splineRibbon;
            ClosedArea = closedArea;
            RawProceduralMesh = rawProceduralMesh;
        }

        public PresenterSurfaceKind Kind { get; }
        public int Version { get; }
        public SurfaceSplineRibbonPayload SplineRibbon { get; }
        public SurfaceClosedAreaPayload ClosedArea { get; }
        public SurfaceRawProceduralMeshPayload RawProceduralMesh { get; }
    }

    public sealed class SurfaceSourcePayloadRegistry
    {
        private readonly Dictionary<int, SurfacePayloadSnapshot> _payloads = new();
        private readonly Dictionary<int, int> _versions = new();

        public void SetSplineRibbon(int scopeId, SurfaceSplineSegment[] segments)
        {
            if (scopeId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeId));
            }

            _payloads[scopeId] = new SurfacePayloadSnapshot(
                PresenterSurfaceKind.SplineRibbon,
                NextVersion(scopeId),
                new SurfaceSplineRibbonPayload(segments),
                default,
                default);
        }

        public void SetClosedArea(int scopeId, Vector3[] boundaryPoints)
        {
            if (scopeId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeId));
            }

            _payloads[scopeId] = new SurfacePayloadSnapshot(
                PresenterSurfaceKind.ClosedArea,
                NextVersion(scopeId),
                default,
                new SurfaceClosedAreaPayload(boundaryPoints),
                default);
        }

        public void SetRawProceduralMesh(int scopeId, ProceduralMeshAssetData proceduralMesh, in Vector3 worldOrigin)
        {
            if (scopeId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeId));
            }

            _payloads[scopeId] = new SurfacePayloadSnapshot(
                PresenterSurfaceKind.RawProceduralMesh,
                NextVersion(scopeId),
                default,
                default,
                new SurfaceRawProceduralMeshPayload(proceduralMesh, worldOrigin));
        }

        public bool Remove(int scopeId)
        {
            return _payloads.Remove(scopeId);
        }

        public bool TryGet(int scopeId, out SurfacePayloadSnapshot snapshot)
        {
            return _payloads.TryGetValue(scopeId, out snapshot);
        }

        private int NextVersion(int scopeId)
        {
            int next = _versions.TryGetValue(scopeId, out int current)
                ? current + 1
                : 1;
            _versions[scopeId] = next;
            return next;
        }
    }
}
