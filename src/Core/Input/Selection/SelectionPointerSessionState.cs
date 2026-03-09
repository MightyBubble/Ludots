using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Input.Selection
{
    public sealed class SelectionPointerSessionState
    {
        private readonly List<Vector2> _polygonPoints = new(64);

        public bool IsTrackingPrimary { get; private set; }
        public Vector2 PressScreen { get; private set; }
        public float ElapsedTimeSec { get; private set; }
        public float LastPrimaryReleaseTimeSec { get; private set; } = float.NegativeInfinity;
        public Vector2 LastPrimaryReleaseScreen { get; private set; }

        public void AdvanceTime(float dt)
        {
            ElapsedTimeSec += Math.Max(0f, dt);
        }

        public void BeginPrimary(Vector2 pointerScreen)
        {
            IsTrackingPrimary = true;
            PressScreen = pointerScreen;
            _polygonPoints.Clear();
            _polygonPoints.Add(pointerScreen);
        }

        public void RecordDragPoint(Vector2 pointerScreen, float minDistancePx)
        {
            if (!IsTrackingPrimary)
            {
                return;
            }

            if (_polygonPoints.Count == 0)
            {
                _polygonPoints.Add(pointerScreen);
                return;
            }

            float minDistanceSq = Math.Max(0f, minDistancePx) * Math.Max(0f, minDistancePx);
            if (Vector2.DistanceSquared(_polygonPoints[_polygonPoints.Count - 1], pointerScreen) >= minDistanceSq)
            {
                _polygonPoints.Add(pointerScreen);
            }
        }

        public Vector2[] SnapshotPolygon(Vector2 currentPointer, float minDistancePx)
        {
            if (_polygonPoints.Count == 0)
            {
                return Array.Empty<Vector2>();
            }

            float minDistanceSq = Math.Max(0f, minDistancePx) * Math.Max(0f, minDistancePx);
            bool appendCurrent = Vector2.DistanceSquared(_polygonPoints[_polygonPoints.Count - 1], currentPointer) >= minDistanceSq;
            int count = _polygonPoints.Count + (appendCurrent ? 1 : 0);
            var polygon = new Vector2[count];
            for (int i = 0; i < _polygonPoints.Count; i++)
            {
                polygon[i] = _polygonPoints[i];
            }

            if (appendCurrent)
            {
                polygon[count - 1] = currentPointer;
            }

            return polygon;
        }

        public void FinishPrimary(Vector2 pointerScreen)
        {
            IsTrackingPrimary = false;
            LastPrimaryReleaseTimeSec = ElapsedTimeSec;
            LastPrimaryReleaseScreen = pointerScreen;
            _polygonPoints.Clear();
        }

        public void CancelPrimary()
        {
            IsTrackingPrimary = false;
            _polygonPoints.Clear();
        }
    }
}
