using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Pure-math viewport and projection utilities. Platform-agnostic.
    /// Used by Core systems for culling, HUD projection, and preset design.
    /// </summary>
    public static class CameraViewportUtil
    {
        public const float DefaultNearPlaneMeters = 0.1f;
        public const float DefaultFarPlaneMeters = 10000f;
        private const float FarPlaneDistanceMultiplier = 8f;

        /// <summary>
        /// Compute viewport extent in logic space (cm) with buffer.
        /// Same formula as CameraCullingSystem.
        /// </summary>
        public static (float widthCm, float heightCm) ComputeViewportExtent(
            float distanceCm, float fovYDeg, float pitchDeg, float aspectRatio,
            float buffer = 1.5f)
        {
            float fovY = WorldPlane2D.DegToRadValue(fovYDeg);
            float pitchRad = WorldPlane2D.DegToRadValue(pitchDeg);

            float logicHeight = 2.0f * distanceCm * (float)Math.Tan(fovY / 2.0f);
            float pitchScale = 1.0f / Math.Max((float)Math.Sin(pitchRad), 0.1f);
            logicHeight *= pitchScale;
            float logicWidth = logicHeight * aspectRatio;

            logicWidth *= buffer;
            logicHeight *= buffer;

            return (logicWidth, logicHeight);
        }

        /// <summary>
        /// Given desired vertical extent (cm), compute required DistanceCm.
        /// </summary>
        public static float DistanceForVerticalExtent(
            float desiredHeightCm, float fovYDeg, float pitchDeg, float buffer = 1.5f)
        {
            float fovY = WorldPlane2D.DegToRadValue(fovYDeg);
            float pitchRad = WorldPlane2D.DegToRadValue(pitchDeg);
            float sinPitch = Math.Max((float)Math.Sin(pitchRad), 0.1f);

            float h0 = desiredHeightCm / (2f * buffer);
            float distanceCm = h0 * sinPitch / (float)Math.Tan(fovY / 2.0);
            return distanceCm;
        }

        /// <summary>
        /// Given desired horizontal extent (cm), compute required DistanceCm.
        /// </summary>
        public static float DistanceForHorizontalExtent(
            float desiredWidthCm, float fovYDeg, float pitchDeg, float aspectRatio, float buffer = 1.5f)
        {
            float desiredHeightCm = desiredWidthCm / aspectRatio;
            return DistanceForVerticalExtent(desiredHeightCm, fovYDeg, pitchDeg, buffer);
        }

        /// <summary>
        /// Project world position (meters, Y-up) to screen pixels.
        /// Returns NaN if behind camera or outside frustum.
        /// </summary>
        public static Vector2 WorldToScreen(
            Vector3 worldM,
            in CameraRenderState3D camera,
            Vector2 resolution,
            float aspectRatio)
        {
            var view = Matrix4x4.CreateLookAt(camera.Position, camera.Target, camera.Up);
            float fovYRad = WorldPlane2D.DegToRadValue(camera.FovYDeg);
            CameraClipPlanes clipPlanes = ResolveClipPlanes(in camera);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovYRad, aspectRatio, clipPlanes.NearMeters, clipPlanes.FarMeters);

            var world4 = new Vector4(worldM, 1f);
            var viewProj = view * proj;
            var clip = Vector4.Transform(world4, viewProj);

            if (clip.W <= 0.001f)
                return new Vector2(float.NaN, float.NaN);

            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;

            if (ndcX < -1f || ndcX > 1f || ndcY < -1f || ndcY > 1f)
                return new Vector2(float.NaN, float.NaN);

            float screenX = (ndcX + 1f) * 0.5f * resolution.X;
            float screenY = (1f - ndcY) * 0.5f * resolution.Y;

            return new Vector2(screenX, screenY);
        }

        /// <summary>
        /// Convert a screen-space pixel position into a world-space ray using the
        /// same camera math as <see cref="WorldToScreen"/>.
        /// </summary>
        public static ScreenRay ScreenToRay(
            Vector2 screenPosition,
            in CameraRenderState3D camera,
            Vector2 resolution,
            float aspectRatio)
        {
            if (resolution.X <= 0f || resolution.Y <= 0f)
            {
                return new ScreenRay(Vector3.Zero, Vector3.UnitZ);
            }

            float ndcX = (screenPosition.X / resolution.X) * 2f - 1f;
            float ndcY = 1f - (screenPosition.Y / resolution.Y) * 2f;

            var view = Matrix4x4.CreateLookAt(camera.Position, camera.Target, camera.Up);
            float fovYRad = WorldPlane2D.DegToRadValue(camera.FovYDeg);
            CameraClipPlanes clipPlanes = ResolveClipPlanes(in camera);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(fovYRad, aspectRatio, clipPlanes.NearMeters, clipPlanes.FarMeters);
            var viewProj = view * projection;
            if (!Matrix4x4.Invert(viewProj, out var invViewProj))
            {
                Vector3 fallbackDir = Vector3.Normalize(camera.Target - camera.Position);
                return new ScreenRay(camera.Position, fallbackDir);
            }

            var nearClip = new Vector4(ndcX, ndcY, 0f, 1f);
            var farClip = new Vector4(ndcX, ndcY, 1f, 1f);
            var nearWorld4 = Vector4.Transform(nearClip, invViewProj);
            var farWorld4 = Vector4.Transform(farClip, invViewProj);
            if (MathF.Abs(nearWorld4.W) < 1e-6f || MathF.Abs(farWorld4.W) < 1e-6f)
            {
                Vector3 fallbackDir = Vector3.Normalize(camera.Target - camera.Position);
                return new ScreenRay(camera.Position, fallbackDir);
            }

            nearWorld4 /= nearWorld4.W;
            farWorld4 /= farWorld4.W;

            var nearWorld = new Vector3(nearWorld4.X, nearWorld4.Y, nearWorld4.Z);
            var farWorld = new Vector3(farWorld4.X, farWorld4.Y, farWorld4.Z);
            var direction = Vector3.Normalize(farWorld - nearWorld);
            return new ScreenRay(nearWorld, direction);
        }

        public static CameraClipPlanes ResolveClipPlanes(in CameraRenderState3D camera)
        {
            float distanceMeters = Vector3.Distance(camera.Position, camera.Target);
            float farMeters = DefaultFarPlaneMeters;
            if (float.IsFinite(distanceMeters) && distanceMeters > 0f)
            {
                farMeters = MathF.Max(farMeters, distanceMeters * FarPlaneDistanceMultiplier);
            }

            return new CameraClipPlanes(DefaultNearPlaneMeters, farMeters);
        }

        /// <summary>
        /// Derive CameraRenderState3D from CameraState (no smoothing).
        /// Same logic as CameraPresenter.
        /// </summary>
        public static CameraRenderState3D StateToRenderState(CameraState state, RenderCameraDebugState cameraDebug = null)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            return StateToRenderState(CameraStateSnapshot.FromState(state), cameraDebug);
        }

        public static CameraRenderState3D StateToRenderState(in CameraStateSnapshot state, RenderCameraDebugState cameraDebug = null)
        {
            Vector3 targetPos = new Vector3(
                WorldUnits.CmToM(state.TargetCm.X),
                WorldUnits.CmToM(state.TargetHeightCm),
                WorldUnits.CmToM(state.TargetCm.Y));

            float yawDeg = state.Yaw + state.ImpulseYawOffsetDeg;
            float pitchDeg = state.Pitch + state.ImpulsePitchOffsetDeg;
            float distanceM = WorldUnits.CmToM(state.DistanceCm);
            if (cameraDebug is { Enabled: true })
            {
                distanceM += cameraDebug.PullBackMeters;
            }

            Vector3 targetToCameraOffset = WorldPlane2D.VisualCameraTargetToCameraOffset(yawDeg, pitchDeg, distanceM);
            Vector3 desiredPos = targetPos + targetToCameraOffset;
            bool firstPerson = state.RigKind == CameraRigKind.FirstPerson || Vector3.DistanceSquared(targetPos, desiredPos) < 0.000001f;
            Vector3 basisForward = firstPerson
                ? WorldPlane2D.VisualCameraForwardFromYawPitchDegrees(yawDeg, pitchDeg)
                : Vector3.Normalize(targetPos - desiredPos);

            Vector3 pivotOffset = ResolveCameraLocalOffsetMeters(state.RigPivotOffsetCm, basisForward);
            targetPos += pivotOffset;

            Vector3 socketOffset = ResolveCameraLocalOffsetMeters(state.RigCameraOffsetCm, basisForward);
            Vector3 impulseOffset = new(
                WorldUnits.CmToM(state.ImpulsePositionOffsetCm.X),
                WorldUnits.CmToM(state.ImpulsePositionOffsetCm.Y),
                WorldUnits.CmToM(state.ImpulsePositionOffsetCm.Z));

            desiredPos = firstPerson
                ? targetPos + socketOffset + impulseOffset
                : targetPos + targetToCameraOffset + socketOffset + impulseOffset;

            if (cameraDebug is { Enabled: true })
            {
                desiredPos += cameraDebug.PositionOffsetMeters;
            }

            Vector3 lookTarget = targetPos;
            Vector3 forward;
            if (firstPerson)
            {
                forward = WorldPlane2D.VisualCameraForwardFromYawPitchDegrees(yawDeg, pitchDeg);
                lookTarget = desiredPos + forward;
            }
            else
            {
                forward = Vector3.Normalize(targetPos - desiredPos);
            }

            Vector3 up = Vector3.UnitY;
            if (Math.Abs(Vector3.Dot(forward, up)) > 0.99f)
                up = Vector3.UnitZ;

            return new CameraRenderState3D(desiredPos, lookTarget, up, state.FovYDeg);
        }

        private static Vector3 ResolveCameraLocalOffsetMeters(Vector3 localOffsetCm, Vector3 viewForward)
        {
            if (localOffsetCm == Vector3.Zero)
            {
                return Vector3.Zero;
            }

            Vector3 planarForward = new(viewForward.X, 0f, viewForward.Z);
            if (planarForward.LengthSquared() <= 0.000001f || !float.IsFinite(planarForward.LengthSquared()))
            {
                planarForward = Vector3.UnitZ;
            }
            else
            {
                planarForward = Vector3.Normalize(planarForward);
            }

            Vector3 right = Vector3.Cross(planarForward, Vector3.UnitY);
            if (right.LengthSquared() <= 0.000001f || !float.IsFinite(right.LengthSquared()))
            {
                right = Vector3.UnitX;
            }
            else
            {
                right = Vector3.Normalize(right);
            }

            return (right * WorldUnits.CmToM(localOffsetCm.X)) +
                   (Vector3.UnitY * WorldUnits.CmToM(localOffsetCm.Y)) +
                   (planarForward * WorldUnits.CmToM(localOffsetCm.Z));
        }
    }
}
