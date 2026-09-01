using System.Numerics;
using Raylib_cs;

namespace Ludots.Raylib.SceneKit
{
    /// <summary>
    /// 引擎检视/编辑视口的相机数学：屏幕坐标反投影为世界射线与射线-盒求交。
    /// 公式与 Core 侧 CameraViewportUtil.ScreenToRay 同源（引擎层零 Core 移植）。
    /// </summary>
    public static class EngineCameraMath
    {
        /// <summary>屏幕像素坐标 → 世界射线（origin 在近平面附近，direction 归一化向前）。</summary>
        public static (Vector3 Origin, Vector3 Direction) ScreenToRay(
            Vector2 screenPosition,
            Camera3D camera,
            float screenWidth,
            float screenHeight)
        {
            if (screenWidth <= 0f || screenHeight <= 0f)
            {
                return (camera.position, Vector3.Normalize(camera.target - camera.position));
            }

            float aspect = screenWidth / screenHeight;
            float ndcX = (screenPosition.X / screenWidth) * 2f - 1f;
            float ndcY = 1f - (screenPosition.Y / screenHeight) * 2f;

            var view = Matrix4x4.CreateLookAt(camera.position, camera.target, camera.up);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(
                camera.fovy * (MathF.PI / 180f), aspect, 0.01f, 10000f);
            if (!Matrix4x4.Invert(view * projection, out Matrix4x4 invViewProj))
            {
                return (camera.position, Vector3.Normalize(camera.target - camera.position));
            }

            var nearWorld = Vector4.Transform(new Vector4(ndcX, ndcY, 0f, 1f), invViewProj);
            var farWorld = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invViewProj);
            if (MathF.Abs(nearWorld.W) < 1e-6f || MathF.Abs(farWorld.W) < 1e-6f)
            {
                return (camera.position, Vector3.Normalize(camera.target - camera.position));
            }

            Vector3 near = new(nearWorld.X / nearWorld.W, nearWorld.Y / nearWorld.W, nearWorld.Z / nearWorld.W);
            Vector3 far = new(farWorld.X / farWorld.W, farWorld.Y / farWorld.W, farWorld.Z / farWorld.W);
            Vector3 direction = far - near;
            float length = direction.Length();
            if (length < 1e-6f)
            {
                return (camera.position, Vector3.Normalize(camera.target - camera.position));
            }

            return (near, direction / length);
        }

        /// <summary>射线 vs 轴对齐包围盒（slab 法），返回命中距离；未命中返回 null。</summary>
        public static float? RayAabbIntersect(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max)
        {
            float tMin = 0f;
            float tMax = float.MaxValue;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = Axis(origin, axis);
                float d = Axis(direction, axis);
                float lo = Axis(min, axis);
                float hi = Axis(max, axis);
                if (MathF.Abs(d) < 1e-8f)
                {
                    if (o < lo || o > hi)
                    {
                        return null;
                    }

                    continue;
                }

                float t1 = (lo - o) / d;
                float t2 = (hi - o) / d;
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                }

                tMin = MathF.Max(tMin, t1);
                tMax = MathF.Min(tMax, t2);
                if (tMin > tMax)
                {
                    return null;
                }
            }

            return tMin;
        }

        /// <summary>射线 vs 旋转盒（OBB）：把射线变换进盒局部空间（仅旋转+平移）后走 slab。</summary>
        public static float? RayObbIntersect(
            Vector3 origin,
            Vector3 direction,
            Vector3 center,
            Vector3 halfExtents,
            Quaternion rotation)
        {
            Quaternion inverse = Quaternion.Normalize(rotation);
            inverse = Quaternion.Conjugate(inverse);
            Vector3 localOrigin = Vector3.Transform(origin - center, inverse);
            Vector3 localDirection = Vector3.Transform(direction, inverse);
            return RayAabbIntersect(localOrigin, localDirection, -halfExtents, halfExtents);
        }

        private static float Axis(Vector3 v, int axis) => axis switch
        {
            0 => v.X,
            1 => v.Y,
            _ => v.Z,
        };
    }
}
