using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>调试绘制：DebugDrawCommandBuffer 手工填充网格/圆/框 + 由相机推得的视锥线，RaylibDebugDrawRenderer 消费。</summary>
    public sealed class DebugDrawScene : IEngineScene
    {
        private readonly DebugDrawCommandBuffer _commands = new();
        private readonly RaylibDebugDrawRenderer _renderer = new() { CircleSegments = 40, PlaneY = 0.02f };

        public string Id => "debug_draw";
        public string Title => "调试绘制";
        public string Summary => "RaylibDebugDrawRenderer + DebugDrawCommandBuffer";

        public void Load()
        {
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 52f);
            float t = (float)totalTimeSeconds;

            Rl.ClearBackground(new Color(12, 14, 20, 255));
            Rl.BeginMode3D(camera);

            Rl.DrawGrid(30, 3f);
            Rl.DrawCube(new Vector3(-9f, 1.1f, 4f), 2.6f, 2.2f, 2.6f, new Color(66, 92, 128, 255));
            Rl.DrawCube(new Vector3(10f, 1.1f, -5f), 2.6f, 2.2f, 2.6f, new Color(128, 84, 66, 255));

            _commands.Clear();
            FillGridLines();
            FillUnitMarkers(t);
            FillCameraFrustum(in camera);
            _renderer.Draw(_commands);

            Rl.EndMode3D();
            GalleryFont.Draw(
                $"debug lines {_commands.Lines.Count}  circles {_commands.Circles.Count}  boxes {_commands.Boxes.Count}",
                12,
                28,
                20,
                GalleryColors.RayWhite);
        }

        private void FillGridLines()
        {
            for (int i = -5; i <= 5; i++)
            {
                if (i == 0)
                {
                    continue;
                }

                var major = new DebugDrawColor(70, 84, 110);
                _commands.Lines.Add(new DebugDrawLine2D
                {
                    A = new Vector2(i * 6f, -30f),
                    B = new Vector2(i * 6f, 30f),
                    Color = major,
                });
                _commands.Lines.Add(new DebugDrawLine2D
                {
                    A = new Vector2(-30f, i * 6f),
                    B = new Vector2(30f, i * 6f),
                    Color = major,
                });
            }

            _commands.Lines.Add(new DebugDrawLine2D { A = new Vector2(-30f, 0f), B = new Vector2(30f, 0f), Color = DebugDrawColor.Red });
            _commands.Lines.Add(new DebugDrawLine2D { A = new Vector2(0f, -30f), B = new Vector2(0f, 30f), Color = DebugDrawColor.Blue });
        }

        private void FillUnitMarkers(float t)
        {
            _commands.Circles.Add(new DebugDrawCircle2D
            {
                Center = new Vector2(-9f, 4f),
                Radius = 3.6f + (MathF.Sin(t * 1.6f) * 0.5f),
                Color = DebugDrawColor.Cyan,
            });
            _commands.Circles.Add(new DebugDrawCircle2D
            {
                Center = new Vector2(10f, -5f),
                Radius = 4.4f + (MathF.Sin(t * 2.1f + 1f) * 0.7f),
                Color = DebugDrawColor.Yellow,
            });
            _commands.Circles.Add(new DebugDrawCircle2D
            {
                Center = new Vector2(0f, 0f),
                Radius = 14f,
                Color = new DebugDrawColor(120, 255, 160),
            });

            float orbit = t * 0.7f;
            _commands.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(MathF.Cos(orbit) * 14f, MathF.Sin(orbit) * 14f),
                HalfWidth = 1.4f,
                HalfHeight = 1.4f,
                RotationRadians = orbit,
                Color = DebugDrawColor.Green,
            });
            _commands.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(-9f, 4f),
                HalfWidth = 1.8f,
                HalfHeight = 1.8f,
                Color = DebugDrawColor.White,
            });
            _commands.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(10f, -5f),
                HalfWidth = 1.8f,
                HalfHeight = 1.8f,
                Color = DebugDrawColor.White,
            });
        }

        private void FillCameraFrustum(in Camera3D camera)
        {
            float aspect = Rl.GetScreenWidth() / (float)Math.Max(1, Rl.GetScreenHeight());
            float fovY = camera.fovy * MathF.PI / 180f;
            float halfH = MathF.Tan(fovY * 0.5f);
            float halfW = halfH * aspect;
            Vector3 forward = Vector3.Normalize(camera.target - camera.position);
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.up));
            Vector3 up = Vector3.Cross(right, forward);
            float far = 60f;

            Vector3 nearCenter = camera.position + (forward * 1.5f);
            Vector3 farCenter = camera.position + (forward * far);
            Vector3[] corners =
            {
                nearCenter + (right * halfW * 1.5f) + (up * halfH * 1.5f),
                nearCenter - (right * halfW * 1.5f) + (up * halfH * 1.5f),
                nearCenter - (right * halfW * 1.5f) - (up * halfH * 1.5f),
                nearCenter + (right * halfW * 1.5f) - (up * halfH * 1.5f),
                farCenter + (right * halfW * far) + (up * halfH * far),
                farCenter - (right * halfW * far) + (up * halfH * far),
                farCenter - (right * halfW * far) - (up * halfH * far),
                farCenter + (right * halfW * far) - (up * halfH * far),
            };

            var frustum = new DebugDrawColor(255, 140, 60);
            void Add(int a, int b)
            {
                _commands.Lines.Add(new DebugDrawLine2D
                {
                    A = new Vector2(corners[a].X, corners[a].Z),
                    B = new Vector2(corners[b].X, corners[b].Z),
                    Color = frustum,
                });
            }

            Add(0, 1);
            Add(1, 2);
            Add(2, 3);
            Add(3, 0);
            Add(4, 5);
            Add(5, 6);
            Add(6, 7);
            Add(7, 4);
            Add(0, 4);
            Add(1, 5);
            Add(2, 6);
            Add(3, 7);
        }

        public void Dispose()
        {
        }
    }
}
