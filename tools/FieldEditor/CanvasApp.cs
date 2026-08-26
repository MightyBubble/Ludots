using System.Numerics;
using System.Text.Json.Nodes;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Tools.FieldEditor;

internal static class CanvasApp
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    private const int SidebarWidth = 280;
    private const int RegionListTop = 154;
    private const int RegionRowHeight = 28;
    private const int FooterHeight = 34;
    private const KeyboardKey DecreaseBrushKey = (KeyboardKey)91;
    private const KeyboardKey IncreaseBrushKey = (KeyboardKey)93;

    public static int Run(CellsDocument document, int maxRegionIds, string assetPath)
    {
        if (OperatingSystem.IsLinux() &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            throw new InvalidOperationException(
                "Raylib canvas requires a graphical display; DISPLAY and WAYLAND_DISPLAY are unset.");
        }

        return new Editor(document, maxRegionIds, assetPath).Run();
    }

    private enum Tool
    {
        Paint,
        Brush,
        Rect,
        Erase,
        Eyedropper,
    }

    private sealed class Editor
    {
        private static readonly Color BackgroundColor = new(22, 24, 31, 255);
        private static readonly Color CanvasColor = new(31, 34, 43, 255);
        private static readonly Color GridColor = new(61, 65, 78, 255);
        private static readonly Color TextColor = new(224, 227, 235, 255);
        private static readonly Color MutedTextColor = new(155, 161, 176, 255);
        private static readonly Color AccentColor = new(85, 180, 255, 255);
        private static readonly Color WarningColor = new(255, 184, 77, 255);

        private readonly CellsDocument _document;
        private readonly int _maxRegionIds;
        private readonly string _assetPath;
        private IReadOnlyDictionary<string, string> _regionColors;
        private string _savedState;
        private bool _isDirty;
        private string? _activeRegion;
        private Tool _tool = Tool.Paint;
        private int _brushRadius = 1;
        private int _regionScroll;
        private float _zoom;
        private float _offsetX;
        private float _offsetY;
        private Vector2 _previousMouse;
        private (int X, int Y)? _hoverCell;
        private (int X, int Y)? _strokeStart;
        private (int X, int Y)? _lastStrokeCell;
        private (int X, int Y)? _lastAppliedCell;
        private JsonObject? _strokeBefore;
        private string? _strokeDocumentBefore;
        private string _status = "Ready. Press S to save changes.";
        private bool _quit;

        public Editor(CellsDocument document, int maxRegionIds, string assetPath)
        {
            _document = document;
            _maxRegionIds = maxRegionIds;
            _assetPath = assetPath;
            _regionColors = FieldEditorMetadataStore.GetColors(assetPath, document);
            _savedState = DocumentState();

            string? storedRegion = HistoryStore.GetActiveBrushKey(assetPath, document.LayerKey);
            _activeRegion = storedRegion != null && document.Regions.ContainsKey(storedRegion)
                ? storedRegion
                : document.Regions.Keys.FirstOrDefault();

            InitializeCamera();
        }

        public int Run()
        {
            bool windowOpened = false;
            try
            {
                Rl.InitWindow(WindowWidth, WindowHeight, "field-editor");
                windowOpened = true;
                Rl.SetExitKey((int)KeyboardKey.KEY_NULL);
                Rl.SetTargetFPS(60);
                _previousMouse = Rl.GetMousePosition();

                while (!_quit)
                {
                    if (Rl.WindowShouldClose())
                    {
                        RequestExit();
                        if (_quit)
                        {
                            break;
                        }
                    }

                    HandleKeyboard();
                    if (_quit)
                    {
                        break;
                    }

                    HandleMouse();
                    Draw();
                }

                return 0;
            }
            finally
            {
                if (windowOpened)
                {
                    Rl.CloseWindow();
                }
            }
        }

        private bool IsDirty => _isDirty;

        private void InitializeCamera()
        {
            (int minX, int minY, int maxX, int maxY) = DisplayBounds();
            double width = (double)maxX - minX + 1;
            double height = (double)maxY - minY + 1;
            float fitX = (float)((WindowWidth - SidebarWidth - 80) / width);
            float fitY = (float)((WindowHeight - 100) / height);
            _zoom = Math.Clamp(Math.Min(fitX, fitY), 5f, 32f);

            double centerX = ((double)minX + maxX + 1) / 2;
            double centerY = ((double)minY + maxY + 1) / 2;
            _offsetX = (float)(SidebarWidth + (WindowWidth - SidebarWidth) / 2.0 - centerX * _zoom);
            _offsetY = (float)(WindowHeight / 2.0 - centerY * _zoom);
        }

        private void HandleKeyboard()
        {
            if (Rl.IsKeyPressed(KeyboardKey.KEY_ONE))
            {
                SelectTool(Tool.Paint);
            }
            else if (Rl.IsKeyPressed(KeyboardKey.KEY_TWO))
            {
                SelectTool(Tool.Brush);
            }
            else if (Rl.IsKeyPressed(KeyboardKey.KEY_THREE))
            {
                SelectTool(Tool.Rect);
            }
            else if (Rl.IsKeyPressed(KeyboardKey.KEY_FOUR))
            {
                SelectTool(Tool.Erase);
            }
            else if (Rl.IsKeyPressed(KeyboardKey.KEY_FIVE))
            {
                SelectTool(Tool.Eyedropper);
            }

            if (Rl.IsKeyPressed(DecreaseBrushKey))
            {
                _brushRadius = Math.Max(0, _brushRadius - 1);
                _status = $"Brush radius: {_brushRadius}.";
            }

            if (Rl.IsKeyPressed(IncreaseBrushKey))
            {
                _brushRadius = Math.Min(128, _brushRadius + 1);
                _status = $"Brush radius: {_brushRadius}.";
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_S))
            {
                CompleteStroke();
                Save();
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_Z))
            {
                CompleteStroke();
                if (HistoryStore.Undo(_assetPath, _document) == null)
                {
                    _status = "Nothing to undo.";
                }
                else
                {
                    ReloadSidecarState();
                    RefreshDirty();
                    _status = "Undo applied. Press S to save.";
                }
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_Y))
            {
                CompleteStroke();
                if (HistoryStore.Redo(_assetPath, _document) == null)
                {
                    _status = "Nothing to redo.";
                }
                else
                {
                    ReloadSidecarState();
                    RefreshDirty();
                    _status = "Redo applied. Press S to save.";
                }
            }

            if (Rl.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
            {
                RequestExit();
            }
        }

        private void SelectTool(Tool tool)
        {
            CompleteStroke();
            _tool = tool;
            _status = $"Tool: {ToolName(tool)}.";
        }

        private void Save()
        {
            try
            {
                _document.Save(_assetPath, _maxRegionIds);
                _savedState = DocumentState();
                _isDirty = false;
                _status = $"Saved {_document.CellCount} cells.";
            }
            catch (InvalidOperationException ex)
            {
                _status = $"Save failed: {ex.Message}";
            }
        }

        private void HandleMouse()
        {
            Vector2 mouse = Rl.GetMousePosition();
            float wheel = Rl.GetMouseWheelMove();

            if (mouse.X < SidebarWidth)
            {
                if (wheel != 0)
                {
                    ScrollRegions(wheel);
                }

                if (Rl.IsMouseButtonPressed(MouseButton.MOUSE_LEFT_BUTTON))
                {
                    SelectRegionAt(mouse);
                }
            }
            else if (wheel != 0)
            {
                ZoomAt(mouse, wheel);
            }

            bool panDown =
                Rl.IsMouseButtonDown(MouseButton.MOUSE_MIDDLE_BUTTON) ||
                Rl.IsMouseButtonDown(MouseButton.MOUSE_RIGHT_BUTTON);
            if (panDown && mouse.X >= SidebarWidth)
            {
                _offsetX += mouse.X - _previousMouse.X;
                _offsetY += mouse.Y - _previousMouse.Y;
            }

            UpdateHoverCell(mouse);
            if (!panDown && mouse.X >= SidebarWidth)
            {
                HandleLeftMouse();
            }

            _previousMouse = mouse;
        }

        private void HandleLeftMouse()
        {
            if (Rl.IsMouseButtonPressed(MouseButton.MOUSE_LEFT_BUTTON) && _hoverCell.HasValue)
            {
                if (_tool == Tool.Eyedropper)
                {
                    PickRegion(_hoverCell.Value);
                    return;
                }

                if (!BeginStroke(_hoverCell.Value))
                {
                    return;
                }

                if (_tool != Tool.Rect)
                {
                    ApplyContinuousTool(_hoverCell.Value);
                }
            }

            if (Rl.IsMouseButtonDown(MouseButton.MOUSE_LEFT_BUTTON) &&
                _strokeBefore != null &&
                _hoverCell.HasValue)
            {
                _lastStrokeCell = _hoverCell.Value;
                if (_tool != Tool.Rect && _lastAppliedCell != _hoverCell)
                {
                    ApplyContinuousTool(_hoverCell.Value);
                }
            }

            if (Rl.IsMouseButtonReleased(MouseButton.MOUSE_LEFT_BUTTON) && _strokeBefore != null)
            {
                if (_tool == Tool.Rect && _strokeStart.HasValue && _lastStrokeCell.HasValue)
                {
                    ApplyRect(_strokeStart.Value, _lastStrokeCell.Value);
                }

                CompleteStroke();
            }
        }

        private bool BeginStroke((int X, int Y) cell)
        {
            if (_tool is Tool.Paint or Tool.Brush or Tool.Rect)
            {
                if (_activeRegion == null)
                {
                    _status = "No active region. Add and select a region first.";
                    return false;
                }

                _document.RegionIndex(_activeRegion);
            }

            _strokeBefore = HistoryStore.CaptureSnapshot(_assetPath, _document);
            _strokeDocumentBefore = DocumentState();
            _strokeStart = cell;
            _lastStrokeCell = cell;
            _lastAppliedCell = null;
            return true;
        }

        private void ApplyContinuousTool((int X, int Y) cell)
        {
            switch (_tool)
            {
                case Tool.Paint:
                    _document.PaintCell(_activeRegion!, cell.X, cell.Y);
                    break;
                case Tool.Brush:
                    _document.PaintRect(
                        _activeRegion!,
                        checked(cell.X - _brushRadius),
                        checked(cell.Y - _brushRadius),
                        checked(cell.X + _brushRadius),
                        checked(cell.Y + _brushRadius));
                    break;
                case Tool.Erase:
                    _document.EraseRect(cell.X, cell.Y, cell.X, cell.Y);
                    break;
            }

            _lastAppliedCell = cell;
        }

        private void ApplyRect((int X, int Y) start, (int X, int Y) end)
        {
            _document.PaintRect(
                _activeRegion!,
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Max(start.X, end.X),
                Math.Max(start.Y, end.Y));
        }

        private void CompleteStroke()
        {
            if (_strokeBefore != null &&
                !string.Equals(_strokeDocumentBefore, DocumentState(), StringComparison.Ordinal))
            {
                HistoryStore.PushSnapshot(_assetPath, _document.LayerKey, _strokeBefore);
                RefreshDirty();
                _status = "Changed. Press S to save.";
            }

            _strokeBefore = null;
            _strokeDocumentBefore = null;
            _strokeStart = null;
            _lastStrokeCell = null;
            _lastAppliedCell = null;
        }

        private void PickRegion((int X, int Y) cell)
        {
            if (!_document.TryGetCellKey(cell.X, cell.Y, out string? regionKey))
            {
                _status = $"Cell ({cell.X},{cell.Y}) is empty.";
                return;
            }

            SetActiveRegion(regionKey!);
            _status = $"Picked '{regionKey}'.";
        }

        private void SetActiveRegion(string regionKey)
        {
            HistoryStore.SetActiveBrushKey(_assetPath, _document, regionKey);
            _activeRegion = regionKey;
        }

        private void ReloadSidecarState()
        {
            _regionColors = FieldEditorMetadataStore.GetColors(_assetPath, _document);
            string? storedRegion =
                HistoryStore.GetActiveBrushKey(_assetPath, _document.LayerKey);
            _activeRegion = storedRegion != null && _document.Regions.ContainsKey(storedRegion)
                ? storedRegion
                : _document.Regions.Keys.FirstOrDefault();
            ClampRegionScroll();
        }

        private void RequestExit()
        {
            CompleteStroke();
            if (IsDirty)
            {
                _status = "Unsaved changes: press S before exiting.";
                return;
            }

            _quit = true;
        }

        private void ZoomAt(Vector2 mouse, float wheel)
        {
            float oldZoom = _zoom;
            float factor = MathF.Pow(1.15f, wheel);
            _zoom = Math.Clamp(_zoom * factor, 5f, 96f);
            float worldX = (mouse.X - _offsetX) / oldZoom;
            float worldY = (mouse.Y - _offsetY) / oldZoom;
            _offsetX = mouse.X - worldX * _zoom;
            _offsetY = mouse.Y - worldY * _zoom;
        }

        private void UpdateHoverCell(Vector2 mouse)
        {
            if (mouse.X < SidebarWidth)
            {
                _hoverCell = null;
                return;
            }

            _hoverCell = (
                ClampToInt(Math.Floor((mouse.X - _offsetX) / _zoom)),
                ClampToInt(Math.Floor((mouse.Y - _offsetY) / _zoom)));
        }

        private void SelectRegionAt(Vector2 mouse)
        {
            int row = (int)((mouse.Y - RegionListTop) / RegionRowHeight);
            if (row < 0 || row >= VisibleRegionRows())
            {
                return;
            }

            string[] keys = _document.Regions.Keys.ToArray();
            int index = _regionScroll + row;
            if (index >= keys.Length)
            {
                return;
            }

            SetActiveRegion(keys[index]);
            _status = $"Active region: '{keys[index]}'.";
        }

        private void ScrollRegions(float wheel)
        {
            _regionScroll -= Math.Sign(wheel) * 3;
            ClampRegionScroll();
        }

        private void ClampRegionScroll()
        {
            int maxScroll = Math.Max(0, _document.Regions.Count - VisibleRegionRows());
            _regionScroll = Math.Clamp(_regionScroll, 0, maxScroll);
        }

        private static int VisibleRegionRows() =>
            Math.Max(1, (WindowHeight - FooterHeight - RegionListTop) / RegionRowHeight);

        private void Draw()
        {
            Rl.BeginDrawing();
            Rl.ClearBackground(BackgroundColor);
            DrawCanvas();
            DrawSidebar();
            DrawStatus();
            Rl.EndDrawing();
        }

        private void DrawCanvas()
        {
            Rl.DrawRectangle(
                SidebarWidth,
                0,
                WindowWidth - SidebarWidth,
                WindowHeight,
                CanvasColor);

            (int boundsMinX, int boundsMinY, int boundsMaxX, int boundsMaxY) = DisplayBounds();
            int viewMinX = ClampToInt(Math.Floor((SidebarWidth - _offsetX) / _zoom) - 1);
            int viewMinY = ClampToInt(Math.Floor(-_offsetY / _zoom) - 1);
            int viewMaxX = ClampToInt(Math.Ceiling((WindowWidth - _offsetX) / _zoom) + 1);
            int viewMaxY = ClampToInt(Math.Ceiling((WindowHeight - _offsetY) / _zoom) + 1);
            int minX = Math.Max(boundsMinX, viewMinX);
            int minY = Math.Max(boundsMinY, viewMinY);
            int maxX = Math.Min(boundsMaxX, viewMaxX);
            int maxY = Math.Min(boundsMaxY, viewMaxY);

            if (minX <= maxX && minY <= maxY)
            {
                for (long y = minY; y <= maxY; y++)
                {
                    for (long x = minX; x <= maxX; x++)
                    {
                        int cellX = (int)x;
                        int cellY = (int)y;
                        if (_document.TryGetCellKey(cellX, cellY, out string? regionKey))
                        {
                            DrawFilledCell(cellX, cellY, ColorFor(regionKey!));
                        }
                    }
                }

                DrawGrid(minX, minY, maxX, maxY);
            }

            DrawToolPreview();
            Rl.DrawText(
                $"[1] Paint  [2] Brush r={_brushRadius}  [3] Rect  [4] Erase  [5] Eyedropper",
                SidebarWidth + 12,
                10,
                18,
                TextColor);
            Rl.DrawText(
                _hoverCell.HasValue
                    ? $"hover ({_hoverCell.Value.X},{_hoverCell.Value.Y})  zoom {_zoom:0.0}px"
                    : $"zoom {_zoom:0.0}px",
                SidebarWidth + 12,
                36,
                17,
                MutedTextColor);
        }

        private void DrawFilledCell(int x, int y, Color color)
        {
            int left = ScreenX(x);
            int top = ScreenY(y);
            int right = ScreenX((long)x + 1);
            int bottom = ScreenY((long)y + 1);
            Rl.DrawRectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top), color);
        }

        private void DrawGrid(int minX, int minY, int maxX, int maxY)
        {
            int left = ScreenX(minX);
            int right = ScreenX((long)maxX + 1);
            int top = ScreenY(minY);
            int bottom = ScreenY((long)maxY + 1);

            for (long x = minX; x <= (long)maxX + 1; x++)
            {
                Rl.DrawRectangle(ScreenX(x), top, 1, Math.Max(1, bottom - top), GridColor);
            }

            for (long y = minY; y <= (long)maxY + 1; y++)
            {
                Rl.DrawRectangle(left, ScreenY(y), Math.Max(1, right - left), 1, GridColor);
            }
        }

        private void DrawToolPreview()
        {
            if (!_hoverCell.HasValue)
            {
                return;
            }

            (int x0, int y0, int x1, int y1) = _tool switch
            {
                Tool.Brush => (
                    _hoverCell.Value.X - _brushRadius,
                    _hoverCell.Value.Y - _brushRadius,
                    _hoverCell.Value.X + _brushRadius,
                    _hoverCell.Value.Y + _brushRadius),
                Tool.Rect when _strokeStart.HasValue => (
                    Math.Min(_strokeStart.Value.X, _hoverCell.Value.X),
                    Math.Min(_strokeStart.Value.Y, _hoverCell.Value.Y),
                    Math.Max(_strokeStart.Value.X, _hoverCell.Value.X),
                    Math.Max(_strokeStart.Value.Y, _hoverCell.Value.Y)),
                _ => (
                    _hoverCell.Value.X,
                    _hoverCell.Value.Y,
                    _hoverCell.Value.X,
                    _hoverCell.Value.Y),
            };

            int left = ScreenX(x0);
            int top = ScreenY(y0);
            int right = ScreenX((long)x1 + 1);
            int bottom = ScreenY((long)y1 + 1);
            Rl.DrawRectangleLines(
                left,
                top,
                Math.Max(1, right - left),
                Math.Max(1, bottom - top),
                AccentColor);
        }

        private void DrawSidebar()
        {
            Rl.DrawRectangle(0, 0, SidebarWidth, WindowHeight, BackgroundColor);
            Rl.DrawRectangle(SidebarWidth - 1, 0, 1, WindowHeight, GridColor);
            Rl.DrawText("FIELD EDITOR", 18, 18, 24, TextColor);
            Rl.DrawText(_document.LayerKey, 18, 51, 18, AccentColor);
            Rl.DrawText($"Tool: {ToolName(_tool)}", 18, 82, 18, TextColor);
            Rl.DrawText(
                $"Regions: {_document.Regions.Count}/{_maxRegionIds}",
                18,
                108,
                18,
                MutedTextColor);
            Rl.DrawText("REGIONS", 18, 132, 16, MutedTextColor);

            string[] keys = _document.Regions.Keys.ToArray();
            int visibleRows = VisibleRegionRows();
            for (int row = 0; row < visibleRows; row++)
            {
                int index = _regionScroll + row;
                if (index >= keys.Length)
                {
                    break;
                }

                string key = keys[index];
                int y = RegionListTop + row * RegionRowHeight;
                bool active = string.Equals(key, _activeRegion, StringComparison.Ordinal);
                if (active)
                {
                    Rl.DrawRectangle(8, y, SidebarWidth - 17, RegionRowHeight - 2, new Color(50, 61, 78, 255));
                }

                Color color = ColorFor(key);
                Rl.DrawRectangle(18, y + 5, 18, 18, color);
                Rl.DrawRectangleLines(18, y + 5, 18, 18, active ? AccentColor : GridColor);
                Rl.DrawText(TrimForSidebar(key), 46, y + 6, 16, active ? TextColor : MutedTextColor);
            }
        }

        private void DrawStatus()
        {
            int y = WindowHeight - FooterHeight;
            Rl.DrawRectangle(SidebarWidth, y, WindowWidth - SidebarWidth, FooterHeight, BackgroundColor);
            Rl.DrawText(
                IsDirty ? $"UNSAVED | {_status}" : _status,
                SidebarWidth + 12,
                y + 8,
                17,
                IsDirty ? WarningColor : MutedTextColor);
        }

        private (int MinX, int MinY, int MaxX, int MaxY) DisplayBounds()
        {
            if (!_document.TryGetBounds(out int minX, out int minY, out int maxX, out int maxY))
            {
                return (0, 0, 31, 31);
            }

            return (
                minX <= int.MinValue + 2 ? int.MinValue : minX - 2,
                minY <= int.MinValue + 2 ? int.MinValue : minY - 2,
                maxX >= int.MaxValue - 2 ? int.MaxValue : maxX + 2,
                maxY >= int.MaxValue - 2 ? int.MaxValue : maxY + 2);
        }

        private Color ColorFor(string key)
        {
            if (_regionColors.TryGetValue(key, out string? color))
            {
                return ParseColor(color);
            }

            uint hash = 2166136261;
            foreach (char value in key)
            {
                hash = unchecked((hash ^ value) * 16777619);
            }

            return new Color(
                (byte)(72 + (hash & 0x7F)),
                (byte)(72 + ((hash >> 8) & 0x7F)),
                (byte)(72 + ((hash >> 16) & 0x7F)),
                255);
        }

        private static Color ParseColor(string value) =>
            new(
                Convert.ToByte(value.Substring(1, 2), 16),
                Convert.ToByte(value.Substring(3, 2), 16),
                Convert.ToByte(value.Substring(5, 2), 16),
                255);

        private string DocumentState() => _document.ToSnapshotJson().ToJsonString();

        private void RefreshDirty()
        {
            _isDirty = !string.Equals(_savedState, DocumentState(), StringComparison.Ordinal);
        }

        private int ScreenX(long cellX) => (int)Math.Floor(_offsetX + cellX * (double)_zoom);

        private int ScreenY(long cellY) => (int)Math.Floor(_offsetY + cellY * (double)_zoom);

        private static int ClampToInt(double value)
        {
            if (value <= int.MinValue)
            {
                return int.MinValue;
            }

            if (value >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)value;
        }

        private static string ToolName(Tool tool) => tool switch
        {
            Tool.Paint => "Paint cell",
            Tool.Brush => "Brush",
            Tool.Rect => "Rectangle fill",
            Tool.Erase => "Erase",
            Tool.Eyedropper => "Eyedropper",
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };

        private static string TrimForSidebar(string key) =>
            key.Length <= 25 ? key : $"{key[..22]}...";
    }
}
