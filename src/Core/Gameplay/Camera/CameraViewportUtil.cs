using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Pure-math viewport and projection utilities. Platform-agnostic.
    /// Used by Core systems for culling, HUD projection, and preset design.
    /// </summary>
    public static class CameraViewportUtil
    {
        private const float NearPlane = 0.1f;
        private const float FarPlane = 10000f;

        /// <summary>
        /// Compute viewport extent in logic space (cm) with buffer.
        /// Same formula as CameraCullingSystem.
        /// </summary>
        public static (float widthCm, float heightCm) ComputeViewportExtent(
            float distanceCm, float fovYDeg, float pitchDeg, float aspectRatio,
            float buffer = 1.5f)
        {
            float fovY = fovYDeg * (float)(Math.PI / 180.0);
            float pitchRad = pitchDeg * (float)(Math.PI / 180.0);

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
            float fovY = fovYDeg * (float)(Math.PI / 180.0);
            float pitchRad = pitchDeg * (float)(Math.PI / 180.0);
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
            float fovYRad = camera.FovYDeg * (float)(Math.PI / 180.0);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovYRad, aspectRatio, NearPlane, FarPlane);

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
        /// Derive CameraRenderState3D from CameraState (no smoothing).
        /// Same logic as CameraPresenter.
        /// </summary>
        public static CameraRenderState3D StateToRenderState(CameraState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            Vector3 targetPos = new Vector3(
                WorldUnits.CmToM(state.TargetCm.X), 0f, WorldUnits.CmToM(state.TargetCm.Y));

            float yawRad = state.Yaw * (float)(Math.PI / 180.0);
            float pitchRad = state.Pitch * (float)(Math.PI / 180.0);
            float distanceM = WorldUnits.CmToM(state.DistanceCm);
            float hDist = distanceM * (float)Math.Cos(pitchRad);
            float vDist = distanceM * (float)Math.Sin(pitchRad);

            float offsetX = hDist * (float)Math.Sin(yawRad);
            float offsetZ = -hDist * (float)Math.Cos(yawRad);
            Vector3 offset = new Vector3(offsetX, vDist, offsetZ);
            Vector3 desiredPos = targetPos + offset;

            Vector3 forward = Vector3.Normalize(targetPos - desiredPos);
            Vector3 up = Vector3.UnitY;
            if (Math.Abs(Vector3.Dot(forward, up)) > 0.99f)
                up = Vector3.UnitZ;

            return new CameraRenderState3D(desiredPos, targetPos, up, state.FovYDeg);
        }
    }
}
