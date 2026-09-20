using System.Numerics;
using Ludots.Raylib.Render;
using Ludots.Raylib.SceneKit;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using SkiaSharp;

namespace Ludots.App.RaylibPlayer
{
    /// <summary>
    /// 场景编辑会话（Edit 态）：左键拾取实例/拖 gizmo 移动，右键旋转检视相机，
    /// Ctrl+S 显式保存（内存编辑模型 → 原子写回 + 冲突 fail-closed）。编辑器是 scene.json 唯一写入方。
    /// </summary>
    public sealed class SceneEditorSession : IDisposable
    {
        private enum DragState
        {
            None,
            Picking,
            DraggingAxis,
        }

        private const int AxisPixelLength = 72;
        private const int AxisHitRadiusPixels = 12;

        private readonly EngineSceneEditorModel _model;
        private readonly List<EngineEditableInstance> _instances;
        private readonly int[] _axisOrder = [0, 1, 2];
        private readonly Vector3[] _axisColors =
        [
            new(0.95f, 0.32f, 0.30f),
            new(0.40f, 0.90f, 0.38f),
            new(0.35f, 0.55f, 0.98f),
        ];

        private RaylibSkiaRenderer? _panel;
        private DragState _dragState;
        private int _selected = -1;
        private int _dragAxis = -1;
        private Vector3 _dragGrabWorld;
        private Vector3 _dragStartPosition;
        private string _status = "Edit：左键拾取 · 拖轴移动 · 右键旋转 · Ctrl+S 保存 · E 退出编辑";

        public SceneEditorSession(EngineSceneEditorModel model)
        {
            _model = model;
            _instances = model.EnumerateStaticMeshInstances();
        }

        public int SelectedIndex => _selected;

        public EngineEditableInstance? Selected => _selected >= 0 && _selected < _instances.Count ? _instances[_selected] : null;

        public bool HasUnsavedChanges { get; private set; }

        /// <summary>每帧处理输入（在相机 Update 之外调用；Edit 态相机旋转键=右键）。</summary>
        public void HandleInput(Camera3D camera, float screenWidth, float screenHeight)
        {
            Vector2 mouse = Rl.GetMousePosition();

            if (_dragState == DragState.DraggingAxis && _selected >= 0)
            {
                HandleAxisDrag(mouse, camera, screenWidth, screenHeight);
                if (Rl.IsMouseButtonReleased(MouseButton.MOUSE_LEFT_BUTTON))
                {
                    _dragState = DragState.None;
                    _status = BuildSelectedStatus("已移动（未保存）");
                }

                return;
            }

            if (_selected >= 0)
            {
                int hoveredAxis = HitTestGizmoAxis(mouse, camera, screenWidth, screenHeight);
                if (hoveredAxis >= 0 && Rl.IsMouseButtonPressed(MouseButton.MOUSE_LEFT_BUTTON))
                {
                    _dragAxis = hoveredAxis;
                    _dragState = DragState.DraggingAxis;
                    EngineEditableInstance selected = _instances[_selected];
                    _dragStartPosition = selected.Position;
                    _dragGrabWorld = RayPlaneIntersect(
                        mouse, camera, screenWidth, screenHeight, selected.Position, PlaneNormalFor(camera));
                    return;
                }
            }

            if (Rl.IsMouseButtonPressed(MouseButton.MOUSE_LEFT_BUTTON))
            {
                int hit = PickInstance(mouse, camera, screenWidth, screenHeight);
                _selected = hit;
                _status = hit >= 0 ? BuildSelectedStatus("已选中") : "Edit：未命中实例";
            }
        }

        public bool TrySave()
        {
            try
            {
                _model.Save();
                HasUnsavedChanges = false;
                _status = BuildSelectedStatus("已保存");
                return true;
            }
            catch (Exception exception)
            {
                _status = "保存失败（fail closed）：" + exception.Message;
                return false;
            }
        }

        /// <summary>世界空间叠加层：选中线框 + 三轴 gizmo（须在 BeginMode3D 内调用）。</summary>
        public void DrawWorldOverlay(Camera3D camera)
        {
            if (_selected < 0)
            {
                return;
            }

            EngineEditableInstance instance = _instances[_selected];
            DrawObbWire(instance, new Color(255, 210, 90, 220));

            float axisLength = WorldLengthForPixelSize(camera, instance.Position, AxisPixelLength);
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 dir = AxisVector(axis);
                Vector3 end = instance.Position + (dir * axisLength);
                Vector3 color = _axisColors[axis];
                bool active = _dragState == DragState.DraggingAxis && _dragAxis == axis;
                float width = active ? 5f : 3f;
                Rl.DrawLine3D(instance.Position, end, new Color(
                    (byte)(color.X * 255f),
                    (byte)(color.Y * 255f),
                    (byte)(color.Z * 255f),
                    255));
                _ = width;
            }
        }

        /// <summary>屏幕空间 HUD：状态 + 属性面板（在 EndDrawing 前调用，2D 态）。</summary>
        public void DrawHud(int screenWidth, int screenHeight)
        {
            _panel ??= new RaylibSkiaRenderer(screenWidth, screenHeight);
            SKCanvas canvas = _panel.Canvas;
            _panel.ClearTransparent();

            float panelWidth = 430f;
            float panelHeight = Selected == null ? 88f : 176f;
            float x = screenWidth - panelWidth - 24f;
            float y = 24f;
            using (var backdrop = new SKPaint { Color = new SKColor(16, 20, 32, 210) })
            {
                canvas.DrawRoundRect(x, y, panelWidth, panelHeight, 12f, 12f, backdrop);
            }

            using (var frame = new SKPaint { Color = new SKColor(120, 200, 255, 220), Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f })
            {
                canvas.DrawRoundRect(x, y, panelWidth, panelHeight, 12f, 12f, frame);
            }

            using var font = new SKFont(SKTypeface.Default, 15f);
            using var titleFont = new SKFont(SKTypeface.Default, 17f);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText("场景编辑器 · Edit", x + 18f, y + 30f, titleFont, paint);
            canvas.DrawText(_status, x + 18f, y + 56f, font, paint);

            if (Selected is EngineEditableInstance selected)
            {
                using var mono = new SKFont(SKTypeface.FromFamilyName("Consolas"), 14.5f);
                using var body = new SKPaint { Color = new SKColor(190, 205, 225, 255), IsAntialias = true };
                canvas.DrawText($"node {selected.NodeId} · component #{selected.ComponentIndex} · instance #{selected.InstanceIndex}", x + 18f, y + 82f, mono, body);
                canvas.DrawText($"position  {selected.Position.X,8:0.00} {selected.Position.Y,8:0.00} {selected.Position.Z,8:0.00}", x + 18f, y + 104f, mono, body);
                canvas.DrawText($"halfsize  {selected.HalfExtents.X,8:0.00} {selected.HalfExtents.Y,8:0.00} {selected.HalfExtents.Z,8:0.00}", x + 18f, y + 124f, mono, body);
                float yawDeg = MathF.Atan2(2f * (selected.Rotation.W * selected.Rotation.Y), 1f - (2f * selected.Rotation.Y * selected.Rotation.Y)) * (180f / MathF.PI);
                canvas.DrawText($"yaw       {yawDeg,8:0.0}°   unsaved: {(HasUnsavedChanges ? "yes" : "no")}", x + 18f, y + 144f, mono, body);
                canvas.DrawText("拖 X/Y/Z 轴柄移动 · Ctrl+S 保存", x + 18f, y + 164f, font, paint);
            }

            _panel.RenderToScreen();
        }

        public void Dispose()
        {
            _panel?.Dispose();
        }

        private int PickInstance(Vector2 mouse, Camera3D camera, float screenWidth, float screenHeight)
        {
            (Vector3 origin, Vector3 direction) = EngineCameraMath.ScreenToRay(mouse, camera, screenWidth, screenHeight);
            int best = -1;
            float bestT = float.MaxValue;
            for (int i = 0; i < _instances.Count; i++)
            {
                EngineEditableInstance instance = _instances[i];
                float? t = EngineCameraMath.RayObbIntersect(origin, direction, instance.Position, instance.HalfExtents, instance.Rotation);
                if (t is > 0f && t < bestT)
                {
                    bestT = t.Value;
                    best = i;
                }
            }

            return best;
        }

        private int HitTestGizmoAxis(Vector2 mouse, Camera3D camera, float screenWidth, float screenHeight)
        {
            if (_selected < 0)
            {
                return -1;
            }

            EngineEditableInstance instance = _instances[_selected];
            float axisLength = WorldLengthForPixelSize(camera, instance.Position, AxisPixelLength);
            Vector2 center = WorldToScreen(instance.Position, camera, screenWidth, screenHeight);
            foreach (int axis in _axisOrder)
            {
                Vector2 end = WorldToScreen(instance.Position + (AxisVector(axis) * axisLength), camera, screenWidth, screenHeight);
                if (DistanceToSegment(mouse, center, end) <= AxisHitRadiusPixels)
                {
                    return axis;
                }
            }

            return -1;
        }

        private void HandleAxisDrag(Vector2 mouse, Camera3D camera, float screenWidth, float screenHeight)
        {
            EngineEditableInstance instance = _instances[_selected];
            Vector3 planeNormal = PlaneNormalFor(camera);
            Vector3 current = RayPlaneIntersect(mouse, camera, screenWidth, screenHeight, _dragStartPosition, planeNormal);
            Vector3 axis = AxisVector(_dragAxis);
            float delta = Vector3.Dot(current - _dragGrabWorld, axis);
            Vector3 moved = _dragStartPosition + (axis * delta);

            instance.PositionArray[0] = System.Text.Json.Nodes.JsonValue.Create(Math.Round(moved.X, 2));
            instance.PositionArray[1] = System.Text.Json.Nodes.JsonValue.Create(Math.Round(moved.Y, 2));
            instance.PositionArray[2] = System.Text.Json.Nodes.JsonValue.Create(Math.Round(moved.Z, 2));
            HasUnsavedChanges = true;
        }

        private Vector3 PlaneNormalFor(Camera3D camera)
        {
            Vector3 forward = Vector3.Normalize(camera.target - camera.position);
            Vector3 axis = AxisVector(_dragAxis >= 0 ? _dragAxis : 0);
            Vector3 normal = Vector3.Cross(forward, axis);
            return normal.LengthSquared() < 1e-4f ? Vector3.Cross(forward, AxisVector(1)) : Vector3.Normalize(normal);
        }

        private static Vector3 RayPlaneIntersect(
            Vector2 mouse, Camera3D camera, float screenWidth, float screenHeight, Vector3 planePoint, Vector3 planeNormal)
        {
            (Vector3 origin, Vector3 direction) = EngineCameraMath.ScreenToRay(mouse, camera, screenWidth, screenHeight);
            float denominator = Vector3.Dot(direction, planeNormal);
            if (MathF.Abs(denominator) < 1e-5f)
            {
                return planePoint;
            }

            float t = Vector3.Dot(planePoint - origin, planeNormal) / denominator;
            return origin + (direction * MathF.Max(t, 0f));
        }

        private static float WorldLengthForPixelSize(Camera3D camera, Vector3 worldPoint, float pixels)
        {
            float distance = Vector3.Distance(camera.position, worldPoint);
            float halfFovRad = camera.fovy * 0.5f * (MathF.PI / 180f);
            float worldPerPixel = (2f * distance * MathF.Tan(halfFovRad)) / 900f;
            return MathF.Max(worldPerPixel * pixels, 0.5f);
        }

        private static Vector2 WorldToScreen(Vector3 world, Camera3D camera, float screenWidth, float screenHeight)
        {
            Vector2 screen = Rl.GetWorldToScreen(world, camera);
            return new Vector2(screen.X * (screenWidth / 1600f), screen.Y * (screenHeight / 900f));
        }

        private static void DrawObbWire(EngineEditableInstance instance, Color color)
        {
            Vector3[] corners = BuildObbCorners(instance);
            int[][] edges =
            [
                [0, 1], [1, 3], [3, 2], [2, 0],
                [4, 5], [5, 7], [7, 6], [6, 4],
                [0, 4], [1, 5], [2, 6], [3, 7],
            ];
            foreach (int[] edge in edges)
            {
                Rl.DrawLine3D(corners[edge[0]], corners[edge[1]], color);
            }
        }

        private static Vector3[] BuildObbCorners(EngineEditableInstance instance)
        {
            Vector3[] corners = new Vector3[8];
            int index = 0;
            for (int ySign = -1; ySign <= 1; ySign += 2)
            {
                for (int zSign = -1; zSign <= 1; zSign += 2)
                {
                    for (int xSign = -1; xSign <= 1; xSign += 2)
                    {
                        Vector3 local = new(xSign * instance.HalfExtents.X, ySign * instance.HalfExtents.Y, zSign * instance.HalfExtents.Z);
                        corners[index++] = instance.Position + Vector3.Transform(local, instance.Rotation);
                    }
                }
            }

            return corners;
        }

        private static Vector3 AxisVector(int axis) => axis switch
        {
            0 => Vector3.UnitX,
            1 => Vector3.UnitY,
            _ => Vector3.UnitZ,
        };

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared();
            if (lengthSquared < 1e-6f)
            {
                return Vector2.Distance(point, a);
            }

            float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
            return Vector2.Distance(point, a + (ab * t));
        }

        private string BuildSelectedStatus(string suffix)
        {
            if (_selected < 0)
            {
                return suffix;
            }

            EngineEditableInstance instance = _instances[_selected];
            return $"{suffix} · {selectedLabel(instance)}";
        }

        private static string selectedLabel(EngineEditableInstance instance)
        {
            return $"{instance.NodeId}/comp{instance.ComponentIndex}/inst{instance.InstanceIndex}";
        }
    }
}
