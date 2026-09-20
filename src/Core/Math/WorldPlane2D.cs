using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.Core.Mathematics.FixedPoint;

using Ludots.Platform.Abstractions;

namespace Ludots.Core.Mathematics
{
    /// <summary>
    /// Shared 2.5D world-plane conventions.
    /// Logic plane is world XY in centimeters; visual plane is XZ in meters, with Y as height.
    /// Facing radians are measured on the logic plane: 0 = +X, PI/2 = +Y.
    /// </summary>
    public static class WorldPlane2D
    {
        public const float TwoPi = MathF.PI * 2f;
        private const float DegToRad = MathF.PI / 180f;
        private const float RadToDeg = 180f / MathF.PI;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 DirectionFromFacingRad(float facingRad)
        {
            return new Vector2(MathF.Cos(facingRad), MathF.Sin(facingRad));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FacingRadFromDirection(float x, float y)
        {
            return MathF.Atan2(y, x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FacingRadFromDirection(in Vector2 direction)
        {
            return FacingRadFromDirection(direction.X, direction.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FacingRadFromDirection(Fix64 x, Fix64 y)
        {
            return Fix64Math.Atan2Fast(y, x).ToFloat();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FacingRadFromDirection(in Fix64Vec2 direction)
        {
            return FacingRadFromDirection(direction.X, direction.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FacingDegreesPositiveFromDirection(Fix64 x, Fix64 y)
        {
            return FacingDegreesPositiveFromFacingRad(Fix64Math.Atan2Fast(y, x));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FacingDegreesPositiveFromDirection(in Fix64Vec2 direction)
        {
            return FacingDegreesPositiveFromDirection(direction.X, direction.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FacingDegreesPositiveFromDirection(int x, int y)
        {
            return FacingDegreesPositiveFromDirection(Fix64.FromInt(x), Fix64.FromInt(y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FacingDegreesPositiveFromFacingRad(Fix64 radians)
        {
            int degrees = (radians * Fix64.FromInt(180) / Fix64.Pi).RoundToInt();
            degrees %= 360;
            return degrees < 0 ? degrees + 360 : degrees;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 OffsetCmFromFacingRad(float facingRad, float lengthCm)
        {
            return DirectionFromFacingRad(facingRad) * lengthCm;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 Fix64OffsetCmFromFacingRad(float facingRad, float lengthCm)
        {
            Vector2 offset = OffsetCmFromFacingRad(facingRad, lengthCm);
            return Fix64Vec2.FromFloat(offset.X, offset.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 Fix64DirectionFromFacingRad(float facingRad)
        {
            Fix64 angle = Fix64.FromFloat(facingRad);
            return Fix64DirectionFromFacingRad(angle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 Fix64DirectionFromFacingRad(Fix64 facingRad)
        {
            return new Fix64Vec2(Fix64Math.Cos(facingRad), Fix64Math.Sin(facingRad));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 Fix64OffsetCmFromFacingRad(Fix64 facingRad, Fix64 lengthCm)
        {
            return Fix64DirectionFromFacingRad(facingRad) * lengthCm;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DegToRadValue(float degrees)
        {
            return degrees * DegToRad;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float NormalizeDegreesPositive(float degrees)
        {
            if (!float.IsFinite(degrees))
            {
                return 0f;
            }

            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LerpAngleDegrees(float from, float to, float t)
        {
            float delta = NormalizeDegreesSigned(to - from);
            return NormalizeDegreesPositive(from + (delta * t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float NormalizeDegreesSigned(float degrees)
        {
            float normalized = NormalizeDegreesPositive(degrees);
            return normalized > 180f ? normalized - 360f : normalized;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CameraYawDegToFacingRad(float yawDeg)
        {
            return DegToRadValue(yawDeg) + (MathF.PI * 0.5f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 CameraForwardFromYawDegrees(float yawDeg)
        {
            return DirectionFromFacingRad(CameraYawDegToFacingRad(yawDeg));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 CameraRightFromYawDegrees(float yawDeg)
        {
            return DirectionFromFacingRad(CameraYawDegToFacingRad(yawDeg) + (MathF.PI * 0.5f));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 NormalizeOrDefault(Vector2 value, Vector2 defaultValue)
        {
            float lengthSquared = value.LengthSquared();
            if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.000001f)
            {
                return defaultValue;
            }

            return value / MathF.Sqrt(lengthSquared);
        }

        /// <summary>
        /// 旋转随相机模式的 minimap 基向量。right 必须取主视图 CreateLookAt 的真实屏幕右向
        /// （forward × up）；若按"屏幕坐标系翻一次"的直觉取反，整张 minimap 会相对主视图镜像。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CameraMinimapBasisFromYawDegrees(float yawDeg, out Vector2 mapRight, out Vector2 mapUp)
        {
            mapRight = CameraRightFromYawDegrees(yawDeg);
            mapUp = CameraForwardFromYawDegrees(yawDeg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 VisualCameraForwardFromYawPitchDegrees(float yawDeg, float pitchDeg)
        {
            return VisualCameraForwardFromYawPitchRad(DegToRadValue(yawDeg), DegToRadValue(pitchDeg));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 VisualCameraForwardFromYawPitchRad(float yawRad, float pitchRad)
        {
            float cosPitch = MathF.Cos(pitchRad);
            return Vector3.Normalize(new Vector3(
                cosPitch * MathF.Sin(yawRad),
                MathF.Sin(pitchRad),
                -cosPitch * MathF.Cos(yawRad)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 VisualCameraTargetToCameraOffset(float yawDeg, float pitchDeg, float distanceM)
        {
            float yawRad = DegToRadValue(yawDeg);
            float pitchRad = DegToRadValue(pitchDeg);
            float horizontalDistance = distanceM * MathF.Cos(pitchRad);
            return new Vector3(
                horizontalDistance * MathF.Sin(yawRad),
                distanceM * MathF.Sin(pitchRad),
                -horizontalDistance * MathF.Cos(yawRad));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 LogicCmToVisualMeters(in Fix64Vec2 logicCm, float heightMeters = 0f)
        {
            return WorldUnitsFix64.WorldCmToVisualMeters(in logicCm, heightMeters);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 LogicCmToVisualMeters(float logicXcm, float logicYcm, float heightMeters = 0f)
        {
            return WorldUnits.WorldCmToVisualMeters(logicXcm, logicYcm, heightMeters);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 VisualMetersToLogicCm(in Vector3 visualMeters)
        {
            return WorldUnits.VisualMetersToWorldCm2(in visualMeters);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void VisualMetersToLogicCm(in Vector3 visualMeters, out float logicXcm, out float logicYcm)
        {
            logicXcm = visualMeters.X * WorldUnits.CmPerMeter;
            logicYcm = visualMeters.Z * WorldUnits.CmPerMeter;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion FacingRadToVisualYRotation(float facingRad)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, -facingRad);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion FacingToVisualYRotation(in Vector2 facing)
        {
            return FacingRadToVisualYRotation(FacingRadFromDirection(facing.X, facing.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ResolveVisualAssetPosition(
            in Vector3 presenterWorldPosition,
            in Quaternion presenterWorldRotation,
            in Vector3 presenterWorldScale,
            in Vector3 localOffset)
        {
            if (localOffset == Vector3.Zero)
            {
                return presenterWorldPosition;
            }

            return VisualMath.TransformVisualLocal(
                presenterWorldPosition,
                presenterWorldRotation,
                VisualMath.NormalizeScale(presenterWorldScale),
                in localOffset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion ResolveVisualAssetRotation(
            in Quaternion presenterWorldRotation,
            in Quaternion localRotation)
        {
            return ComposeVisualRotation(presenterWorldRotation, localRotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion ComposeVisualRotation(Quaternion worldRotation, Quaternion localRotation)
        {
            return VisualMath.NormalizeOrIdentity(VisualMath.NormalizeOrIdentity(worldRotation) * VisualMath.NormalizeOrIdentity(localRotation));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        /// <summary>
        /// 解出朝向→屏幕角的缓存偏移。det=+1 基（固定北向）屏幕角 = offset − facing；
        /// det=−1 基（旋转随相机、与主视图同手性）屏幕角 = facing − offset，是反射不是旋转，
        /// 常数偏移无法表达，必须由 <paramref name="reflectedBasis"/> 随偏移一起缓存。
        /// </summary>
        public static float ResolveScreenFacingOffsetRad(in Vector2 mapRight, in Vector2 mapUp, out bool reflectedBasis)
        {
            reflectedBasis = ((mapRight.X * mapUp.Y) - (mapRight.Y * mapUp.X)) < 0f;
            return MathF.Atan2(mapRight.Y, mapRight.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ProjectFacingRadToScreen(float facingRad, float screenFacingOffsetRad, bool reflectedBasis)
        {
            return NormalizeSignedRad(reflectedBasis
                ? facingRad - screenFacingOffsetRad
                : screenFacingOffsetRad - facingRad);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ProjectFacingRadToScreenBucket(float facingRad, float screenFacingOffsetRad, bool reflectedBasis, int bucketCount)
        {
            return QuantizeFacingRadToBucket(reflectedBasis
                ? facingRad - screenFacingOffsetRad
                : screenFacingOffsetRad - facingRad, bucketCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ProjectFacingRadToScreen(float facingRad, float rightX, float rightY, float upX, float upY)
        {
            Vector2 facing = DirectionFromFacingRad(facingRad);
            float localRight = (facing.X * rightX) + (facing.Y * rightY);
            float localUp = (facing.X * upX) + (facing.Y * upY);
            return MathF.Atan2(-localUp, localRight);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ProjectFacingRadToScreen(float facingRad, in Vector2 mapRight, in Vector2 mapUp)
        {
            return ProjectFacingRadToScreen(facingRad, mapRight.X, mapRight.Y, mapUp.X, mapUp.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryProjectWorldCmToScreen(
            float worldXcm,
            float worldYcm,
            float centerXcm,
            float centerYcm,
            float rightX,
            float rightY,
            float upX,
            float upY,
            float halfExtentCm,
            float fieldX,
            float fieldY,
            float fieldScale,
            out float normalizedX,
            out float normalizedY,
            out float screenX,
            out float screenY)
        {
            if (!TryWorldToMapNormalized(
                    worldXcm,
                    worldYcm,
                    centerXcm,
                    centerYcm,
                    rightX,
                    rightY,
                    upX,
                    upY,
                    halfExtentCm,
                    out normalizedX,
                    out normalizedY))
            {
                screenX = 0f;
                screenY = 0f;
                return false;
            }

            screenX = fieldX + (normalizedX * fieldScale);
            screenY = fieldY + ((1f - normalizedY) * fieldScale);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryProjectWorldCmToScreenAxisAligned(
            float worldXcm,
            float worldYcm,
            float centerXcm,
            float centerYcm,
            float halfExtentCm,
            float fieldX,
            float fieldY,
            float fieldScale,
            out float normalizedX,
            out float normalizedY,
            out float screenX,
            out float screenY)
        {
            if (!TryWorldToMapNormalizedAxisAligned(
                    worldXcm,
                    worldYcm,
                    centerXcm,
                    centerYcm,
                    halfExtentCm,
                    out normalizedX,
                    out normalizedY))
            {
                screenX = 0f;
                screenY = 0f;
                return false;
            }

            screenX = fieldX + (normalizedX * fieldScale);
            screenY = fieldY + ((1f - normalizedY) * fieldScale);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ProjectWorldCmToScreenUnclipped(
            float worldXcm,
            float worldYcm,
            float centerXcm,
            float centerYcm,
            float rightX,
            float rightY,
            float upX,
            float upY,
            float halfExtentCm,
            float fieldX,
            float fieldY,
            float fieldScale,
            out float screenX,
            out float screenY)
        {
            WorldToMapNormalizedUnclipped(
                worldXcm,
                worldYcm,
                centerXcm,
                centerYcm,
                rightX,
                rightY,
                upX,
                upY,
                halfExtentCm,
                out float normalizedX,
                out float normalizedY);
            screenX = fieldX + (normalizedX * fieldScale);
            screenY = fieldY + ((1f - normalizedY) * fieldScale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WorldToMapNormalizedUnclipped(
            float worldXcm,
            float worldYcm,
            float centerXcm,
            float centerYcm,
            float rightX,
            float rightY,
            float upX,
            float upY,
            float halfExtentCm,
            out float normalizedX,
            out float normalizedY)
        {
            float deltaX = worldXcm - centerXcm;
            float deltaY = worldYcm - centerYcm;
            float localXcm = (deltaX * rightX) + (deltaY * rightY);
            float localYcm = (deltaX * upX) + (deltaY * upY);
            float invExtent = 1f / MathF.Max(1f, halfExtentCm * 2f);
            normalizedX = (localXcm + halfExtentCm) * invExtent;
            normalizedY = (localYcm + halfExtentCm) * invExtent;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WorldToMapNormalizedAxisAlignedUnclipped(
            float worldXcm,
            float worldYcm,
            float centerXcm,
            float centerYcm,
            float halfExtentCm,
            out float normalizedX,
            out float normalizedY)
        {
            float invExtent = 1f / MathF.Max(1f, halfExtentCm * 2f);
            normalizedX = (worldXcm - centerXcm + halfExtentCm) * invExtent;
            normalizedY = (worldYcm - centerYcm + halfExtentCm) * invExtent;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWorldToMapNormalized(
            float worldXcm,
            float worldYcm,
            float centerXcm,
            float centerYcm,
            float rightX,
            float rightY,
            float upX,
            float upY,
            float halfExtentCm,
            out float normalizedX,
            out float normalizedY)
        {
            WorldToMapNormalizedUnclipped(
                worldXcm,
                worldYcm,
                centerXcm,
                centerYcm,
                rightX,
                rightY,
                upX,
                upY,
                halfExtentCm,
                out normalizedX,
                out normalizedY);
            return normalizedX >= 0f && normalizedX <= 1f && normalizedY >= 0f && normalizedY <= 1f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWorldToMapNormalizedAxisAligned(
            float worldXcm,
            float worldYcm,
            float centerXcm,
            float centerYcm,
            float halfExtentCm,
            out float normalizedX,
            out float normalizedY)
        {
            WorldToMapNormalizedAxisAlignedUnclipped(
                worldXcm,
                worldYcm,
                centerXcm,
                centerYcm,
                halfExtentCm,
                out normalizedX,
                out normalizedY);
            return normalizedX >= 0f && normalizedX <= 1f && normalizedY >= 0f && normalizedY <= 1f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 MapLocalOffsetToWorld(float localXcm, float localYcm, in Vector2 mapRight, in Vector2 mapUp)
        {
            return (mapRight * localXcm) + (mapUp * localYcm);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ScreenToMapLocal(
            float screenX,
            float screenY,
            float fieldX,
            float fieldY,
            float fieldScale,
            float halfExtentCm,
            bool clampToField,
            out float localXcm,
            out float localYcm)
        {
            float normalizedX = (screenX - fieldX) / MathF.Max(1f, fieldScale);
            float normalizedY = 1f - ((screenY - fieldY) / MathF.Max(1f, fieldScale));
            if (clampToField)
            {
                normalizedX = Math.Clamp(normalizedX, 0f, 1f);
                normalizedY = Math.Clamp(normalizedY, 0f, 1f);
            }

            localXcm = (normalizedX * halfExtentCm * 2f) - halfExtentCm;
            localYcm = (normalizedY * halfExtentCm * 2f) - halfExtentCm;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 MapLocalToWorld(
            float centerXcm,
            float centerYcm,
            float localXcm,
            float localYcm,
            in Vector2 mapRight,
            in Vector2 mapUp)
        {
            Vector2 offset = MapLocalOffsetToWorld(localXcm, localYcm, in mapRight, in mapUp);
            return new Vector2(centerXcm + offset.X, centerYcm + offset.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryIntersectVisualGroundPlane(
            in Vector3 rayOriginMeters,
            in Vector3 rayDirectionMeters,
            float planeYMeters,
            float maxDirectionY,
            out Vector2 worldCm)
        {
            worldCm = default;
            if (!float.IsFinite(rayOriginMeters.X) ||
                !float.IsFinite(rayOriginMeters.Y) ||
                !float.IsFinite(rayOriginMeters.Z) ||
                !float.IsFinite(rayDirectionMeters.X) ||
                !float.IsFinite(rayDirectionMeters.Y) ||
                !float.IsFinite(rayDirectionMeters.Z) ||
                rayDirectionMeters.Y >= maxDirectionY)
            {
                return false;
            }

            float t = (planeYMeters - rayOriginMeters.Y) / rayDirectionMeters.Y;
            if (!float.IsFinite(t) || t <= 0f)
            {
                return false;
            }

            Vector3 hit = rayOriginMeters + (rayDirectionMeters * t);
            VisualMetersToLogicCm(in hit, out float logicXcm, out float logicYcm);
            worldCm = new Vector2(logicXcm, logicYcm);
            return float.IsFinite(worldCm.X) && float.IsFinite(worldCm.Y);
        }

        /// <summary>
        /// 将凸多边形（顶点按绕序排列）裁剪到 [0,1]² 归一化地图矩形（Sutherland–Hodgman）。
        /// 输出保持绕序且仍为凸，写入 <paramref name="result"/>（不得与输入重叠），返回顶点数；
        /// 完全在矩形外返回 0。越界的视锥足迹只能用它取交集——逐顶点独立 clamp 会把凸四边形
        /// 折叠成自相交的蝴蝶结，不是裁剪。
        /// </summary>
        public static int ClipConvexPolygonToUnitSquare(ReadOnlySpan<Vector2> polygon, Span<Vector2> result)
        {
            if (polygon.Length < 3 || polygon.Length > 28 || result.Length < polygon.Length + 4)
            {
                return 0;
            }

            Span<Vector2> front = stackalloc Vector2[polygon.Length + 4];
            Span<Vector2> back = stackalloc Vector2[polygon.Length + 4];
            polygon.CopyTo(front);

            int count = ClipPolygonToHalfPlane(front.Slice(0, polygon.Length), back, axisX: true, boundary: 0f, keepGreaterEqual: true);
            if (count > 0)
            {
                count = ClipPolygonToHalfPlane(back.Slice(0, count), front, axisX: true, boundary: 1f, keepGreaterEqual: false);
            }

            if (count > 0)
            {
                count = ClipPolygonToHalfPlane(front.Slice(0, count), back, axisX: false, boundary: 0f, keepGreaterEqual: true);
            }

            if (count > 0)
            {
                count = ClipPolygonToHalfPlane(back.Slice(0, count), front, axisX: false, boundary: 1f, keepGreaterEqual: false);
            }

            if (count > 0)
            {
                front.Slice(0, count).CopyTo(result);
            }

            return count;
        }

        private static int ClipPolygonToHalfPlane(
            ReadOnlySpan<Vector2> source,
            Span<Vector2> destination,
            bool axisX,
            float boundary,
            bool keepGreaterEqual)
        {
            int count = 0;
            for (int i = 0; i < source.Length; i++)
            {
                Vector2 current = source[i];
                Vector2 next = source[(i + 1) % source.Length];
                float currentV = axisX ? current.X : current.Y;
                float nextV = axisX ? next.X : next.Y;
                bool currentIn = keepGreaterEqual ? currentV >= boundary : currentV <= boundary;
                bool nextIn = keepGreaterEqual ? nextV >= boundary : nextV <= boundary;
                if (currentIn)
                {
                    destination[count++] = current;
                    if (!nextIn)
                    {
                        float t = (boundary - currentV) / (nextV - currentV);
                        destination[count++] = current + ((next - current) * t);
                    }
                }
                else if (nextIn)
                {
                    float t = (boundary - currentV) / (nextV - currentV);
                    destination[count++] = current + ((next - current) * t);
                }
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float NormalizePositiveRad(float radians)
        {
            if (!float.IsFinite(radians))
            {
                return 0f;
            }

            float normalized = radians % TwoPi;
            return normalized < 0f ? normalized + TwoPi : normalized;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float NormalizeSignedRad(float radians)
        {
            float normalized = NormalizePositiveRad(radians);
            return normalized > MathF.PI ? normalized - TwoPi : normalized;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AngleDistanceRad(float a, float b)
        {
            return MathF.Abs(NormalizeSignedRad(a - b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int QuantizeFacingRadToBucket(float radians, int bucketCount)
        {
            if (bucketCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bucketCount));
            }

            if (!float.IsFinite(radians))
            {
                return 0;
            }

            float normalized = NormalizePositiveRad(radians);
            int bucket = (int)MathF.Round(normalized * bucketCount / TwoPi);
            return bucket >= bucketCount ? 0 : bucket;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NormalizeBucketIndex(int bucket, int bucketCount)
        {
            if (bucketCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bucketCount));
            }

            int normalized = bucket % bucketCount;
            return normalized < 0 ? normalized + bucketCount : normalized;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BucketToFacingRad(int bucket, int bucketCount)
        {
            if (bucketCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bucketCount));
            }

            return NormalizeBucketIndex(bucket, bucketCount) * TwoPi / bucketCount;
        }
    }
}
