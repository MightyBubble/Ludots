using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Utils
{
    /// <summary>
    /// 通用地面射线求交工具。
    /// 将屏幕射线与 Y=0 地面平面求交，返回世界厘米坐标。
    /// </summary>
    public static class GroundRaycastUtil
    {
        /// <summary>
        /// 屏幕射线与 Y=0 地面求交，返回交点的世界厘米整数坐标。
        /// </summary>
        public static bool TryGetGroundWorldCm(in ScreenRay ray, out WorldCmInt2 worldCm)
        {
            worldCm = default;

            float dirY = ray.Direction.Y;
            if (!float.IsFinite(dirY)) return false;
            if (MathF.Abs(dirY) < 1e-6f) return false;

            float originY = ray.Origin.Y;
            if (!float.IsFinite(originY)) return false;

            float t = -originY / dirY;
            if (!float.IsFinite(t) || t < 0f) return false;

            Vector3 hit = ray.Origin + ray.Direction * t;
            if (!float.IsFinite(hit.X) || !float.IsFinite(hit.Z)) return false;

            worldCm = new WorldCmInt2(WorldUnits.MToCm(hit.X), WorldUnits.MToCm(hit.Z));
            return true;
        }

        /// <summary>
        /// 屏幕射线与 Y=0 地面求交，返回交点的视觉空间米坐标 (XZ 平面)。
        /// </summary>
        public static bool TryGetGroundWorldCmBounded(in ScreenRay ray, in WorldSizeSpec worldSize, out WorldCmInt2 worldCm)
        {
            return TryGetGroundWorldCmBounded(in ray, worldSize.Bounds, out worldCm, out _);
        }

        public static bool TryGetGroundWorldCmBounded(in ScreenRay ray, in WorldSizeSpec worldSize, out WorldCmInt2 worldCm, out bool wasClamped)
        {
            return TryGetGroundWorldCmBounded(in ray, worldSize.Bounds, out worldCm, out wasClamped);
        }

        public static bool TryGetGroundWorldCmBounded(in ScreenRay ray, in WorldAabbCm bounds, out WorldCmInt2 worldCm, out bool wasClamped)
        {
            worldCm = default;
            wasClamped = false;
            if (!TryGetGroundWorldCm(in ray, out var rawWorldCm))
            {
                return false;
            }

            worldCm = ClampWorldCmToBounds(rawWorldCm, bounds, out wasClamped);
            return true;
        }

        public static WorldCmInt2 ClampWorldCmToBounds(in WorldCmInt2 worldCm, in WorldAabbCm bounds, out bool wasClamped)
        {
            int x = Math.Clamp(worldCm.X, bounds.Left, bounds.Right);
            int y = Math.Clamp(worldCm.Y, bounds.Top, bounds.Bottom);
            wasClamped = x != worldCm.X || y != worldCm.Y;
            return new WorldCmInt2(x, y);
        }

        public static bool TryGetGroundVisualMeters(in ScreenRay ray, out Vector3 hitMeters)
        {
            hitMeters = default;

            float dirY = ray.Direction.Y;
            if (!float.IsFinite(dirY)) return false;
            if (MathF.Abs(dirY) < 1e-6f) return false;

            float originY = ray.Origin.Y;
            if (!float.IsFinite(originY)) return false;

            float t = -originY / dirY;
            if (!float.IsFinite(t) || t < 0f) return false;

            Vector3 hit = ray.Origin + ray.Direction * t;
            if (!float.IsFinite(hit.X) || !float.IsFinite(hit.Z)) return false;

            hitMeters = hit;
            return true;
        }
    }
}
