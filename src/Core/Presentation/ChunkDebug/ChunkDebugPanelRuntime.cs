using System;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;

namespace Ludots.Core.Presentation.ChunkDebug
{
    public sealed class ChunkDebugPanelRuntime
    {
        private const int PanelX = 18;
        private const int PanelY = 468;
        private const int PanelWidth = 380;
        private const int PanelHeight = 258;
        private const int MapInset = 16;
        private const int MapSize = 168;
        private const int StableBase = 180000;

        public bool Visible { get; set; }

        public void Render(GameEngine engine, ScreenOverlayBuffer overlay)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(overlay);
            if (!Visible)
            {
                return;
            }

            IBoard? board = engine.CurrentMapSession?.PrimaryBoard;
            WorldAabbCm bounds = board?.WorldSize.Bounds ?? engine.WorldSizeSpec.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                RenderMissingBounds(overlay);
                return;
            }

            BoardConfig? boardConfig = ResolveBoardConfig(engine, board);
            int chunkSizeCm = ResolveChunkSizeCm(board, boardConfig, engine.WorldSizeSpec);
            ILoadedChunks? loadedChunks = board?.LoadedChunks ?? engine.GetService(CoreServiceKeys.LoadedChunks);
            CameraCullingDebugState? cull = engine.GetService(CoreServiceKeys.CameraCullingDebugState);
            Vector2 cameraTarget = cull?.CameraTargetCm ?? engine.GameSession.Camera.State.TargetCm;

            int activeChunkCount = loadedChunks?.ActiveChunkKeys.Count ?? 0;
            int minChunkX = FloorDiv(bounds.Left, chunkSizeCm);
            int maxChunkX = FloorDiv(bounds.Right - 1, chunkSizeCm);
            int minChunkY = FloorDiv(bounds.Top, chunkSizeCm);
            int maxChunkY = FloorDiv(bounds.Bottom - 1, chunkSizeCm);
            int cameraChunkX = FloorDiv((int)MathF.Floor(cameraTarget.X), chunkSizeCm);
            int cameraChunkY = FloorDiv((int)MathF.Floor(cameraTarget.Y), chunkSizeCm);

            DrawPanel(overlay, board, boardConfig, bounds, chunkSizeCm, loadedChunks != null, activeChunkCount, cameraTarget, cameraChunkX, cameraChunkY, cull);
            DrawMap(overlay, bounds, chunkSizeCm, loadedChunks, cull, cameraTarget, minChunkX, maxChunkX, minChunkY, maxChunkY);
        }

        private static void RenderMissingBounds(ScreenOverlayBuffer overlay)
        {
            overlay.AddRect(
                PanelX,
                PanelY,
                PanelWidth,
                92,
                new Vector4(0.06f, 0.05f, 0.04f, 0.92f),
                new Vector4(0.98f, 0.45f, 0.28f, 0.98f),
                StableBase,
                1);
            overlay.AddText(
                PanelX + 16,
                PanelY + 18,
                "Chunk Debug",
                18,
                new Vector4(1f, 0.94f, 0.86f, 1f),
                StableBase + 1,
                1);
            overlay.AddText(
                PanelX + 16,
                PanelY + 48,
                "ERR bounds missing",
                14,
                new Vector4(1f, 0.66f, 0.48f, 1f),
                StableBase + 2,
                1);
        }

        private static void DrawPanel(
            ScreenOverlayBuffer overlay,
            IBoard? board,
            BoardConfig? boardConfig,
            in WorldAabbCm bounds,
            int chunkSizeCm,
            bool hasLoadedChunks,
            int activeChunkCount,
            Vector2 cameraTarget,
            int cameraChunkX,
            int cameraChunkY,
            CameraCullingDebugState? cull)
        {
            overlay.AddRect(
                PanelX,
                PanelY,
                PanelWidth,
                PanelHeight,
                new Vector4(0.025f, 0.035f, 0.045f, 0.94f),
                new Vector4(0.34f, 0.54f, 0.62f, 0.95f),
                StableBase,
                1);
            overlay.AddText(PanelX + 16, PanelY + 14, "Chunk Debug", 18, new Vector4(0.96f, 0.98f, 1f, 1f), StableBase + 1, 1);
            overlay.AddText(
                PanelX + 16,
                PanelY + 44,
                $"Board {board?.Name ?? "<none>"} | {boardConfig?.SpatialType ?? board?.GetType().Name ?? "<none>"}",
                13,
                new Vector4(0.77f, 0.86f, 0.91f, 1f),
                StableBase + 2,
                Hash(board?.Name, boardConfig?.SpatialType));
            overlay.AddText(
                PanelX + 16,
                PanelY + 68,
                $"World {bounds.Width / 100f:0}m x {bounds.Height / 100f:0}m",
                13,
                new Vector4(0.79f, 0.88f, 0.94f, 1f),
                StableBase + 3,
                bounds.GetHashCode());
            overlay.AddText(
                PanelX + 16,
                PanelY + 92,
                hasLoadedChunks
                    ? $"Chunk {chunkSizeCm / 100f:0}m | loaded {activeChunkCount}"
                    : $"Chunk {chunkSizeCm / 100f:0}m | loaded n/a (no source)",
                13,
                new Vector4(0.92f, 0.80f, 0.50f, 1f),
                StableBase + 4,
                Hash(chunkSizeCm, activeChunkCount));
            overlay.AddText(
                PanelX + 16,
                PanelY + 116,
                $"Camera ({cameraTarget.X:0},{cameraTarget.Y:0}) cm",
                13,
                new Vector4(0.66f, 0.84f, 0.96f, 1f),
                StableBase + 5,
                Hash(cameraTarget.X, cameraTarget.Y));
            overlay.AddText(
                PanelX + 16,
                PanelY + 140,
                $"Camera chunk ({cameraChunkX},{cameraChunkY})",
                13,
                new Vector4(0.66f, 0.84f, 0.96f, 1f),
                StableBase + 6,
                Hash(cameraChunkX, cameraChunkY));

            if (cull != null)
            {
                overlay.AddText(
                    PanelX + 16,
                    PanelY + 164,
                    $"Cull vis {cull.VisibleEntityCount} | rev {cull.VisibilityRevision}",
                    13,
                    new Vector4(0.80f, 0.90f, 0.72f, 1f),
                    StableBase + 7,
                    Hash(cull.VisibleEntityCount, cull.VisibilityRevision));
            }
        }

        private static void DrawMap(
            ScreenOverlayBuffer overlay,
            in WorldAabbCm bounds,
            int chunkSizeCm,
            ILoadedChunks? loadedChunks,
            CameraCullingDebugState? cull,
            Vector2 cameraTarget,
            int minChunkX,
            int maxChunkX,
            int minChunkY,
            int maxChunkY)
        {
            int mapX = PanelX + PanelWidth - MapInset - MapSize;
            int mapY = PanelY + 64;
            overlay.AddRect(
                mapX,
                mapY,
                MapSize,
                MapSize,
                new Vector4(0.008f, 0.020f, 0.026f, 0.98f),
                new Vector4(0.24f, 0.41f, 0.49f, 0.95f),
                StableBase + 20,
                1);

            int chunkCountX = Math.Max(1, maxChunkX - minChunkX + 1);
            int chunkCountY = Math.Max(1, maxChunkY - minChunkY + 1);
            int strideX = Math.Max(1, chunkCountX / 12);
            int strideY = Math.Max(1, chunkCountY / 12);
            for (int cx = minChunkX + strideX; cx < maxChunkX; cx += strideX)
            {
                int x = mapX + (int)MathF.Round(((cx - minChunkX) / (float)chunkCountX) * (MapSize - 1));
                overlay.AddRect(x, mapY, 1, MapSize, Vector4.Zero, new Vector4(0.18f, 0.31f, 0.37f, 0.72f), StableBase + 21 + cx - minChunkX, 1);
            }

            for (int cy = minChunkY + strideY; cy < maxChunkY; cy += strideY)
            {
                int y = mapY + (int)MathF.Round(((cy - minChunkY) / (float)chunkCountY) * (MapSize - 1));
                overlay.AddRect(mapX, y, MapSize, 1, Vector4.Zero, new Vector4(0.18f, 0.31f, 0.37f, 0.72f), StableBase + 80 + cy - minChunkY, 1);
            }

            if (loadedChunks != null)
            {
                int drawn = 0;
                foreach (long key in loadedChunks.ActiveChunkKeys)
                {
                    if (drawn >= 384)
                    {
                        break;
                    }

                    (int chunkX, int chunkY) = GraphChunkKey.Unpack(key);
                    if (chunkX < minChunkX || chunkX > maxChunkX || chunkY < minChunkY || chunkY > maxChunkY)
                    {
                        continue;
                    }

                    int x = mapX + (int)MathF.Round(((chunkX - minChunkX) / (float)chunkCountX) * (MapSize - 1));
                    int y = mapY + (int)MathF.Round(((chunkY - minChunkY) / (float)chunkCountY) * (MapSize - 1));
                    overlay.AddRect(x - 2, y - 2, 4, 4, new Vector4(0.26f, 0.62f, 0.82f, 0.78f), Vector4.Zero, StableBase + 200 + drawn, Hash(chunkX, chunkY));
                    drawn++;
                }
            }

            if (cull != null)
            {
                DrawWorldRect(overlay, bounds, mapX, mapY, cull.MinX, cull.MinY, cull.MaxX, cull.MaxY, new Vector4(0.95f, 0.74f, 0.28f, 0.96f), StableBase + 700);
            }

            if (TryWorldToMap(bounds, mapX, mapY, cameraTarget.X, cameraTarget.Y, out int cameraX, out int cameraY))
            {
                Vector4 camera = new(1f, 0.95f, 0.54f, 1f);
                overlay.AddRect(cameraX - 5, cameraY - 1, 10, 2, Vector4.Zero, camera, StableBase + 760, Hash(cameraTarget.X, cameraTarget.Y));
                overlay.AddRect(cameraX - 1, cameraY - 5, 2, 10, Vector4.Zero, camera, StableBase + 761, Hash(cameraTarget.Y, cameraTarget.X));
            }
        }

        private static void DrawWorldRect(
            ScreenOverlayBuffer overlay,
            in WorldAabbCm bounds,
            int mapX,
            int mapY,
            float minXcm,
            float minYcm,
            float maxXcm,
            float maxYcm,
            Vector4 color,
            int stableId)
        {
            if (!TryWorldToMap(bounds, mapX, mapY, minXcm, minYcm, out int x0, out int y0) ||
                !TryWorldToMap(bounds, mapX, mapY, maxXcm, maxYcm, out int x1, out int y1))
            {
                return;
            }

            int left = Math.Clamp(Math.Min(x0, x1), mapX, mapX + MapSize - 1);
            int right = Math.Clamp(Math.Max(x0, x1), mapX, mapX + MapSize - 1);
            int top = Math.Clamp(Math.Min(y0, y1), mapY, mapY + MapSize - 1);
            int bottom = Math.Clamp(Math.Max(y0, y1), mapY, mapY + MapSize - 1);
            int width = Math.Max(4, right - left);
            int height = Math.Max(4, bottom - top);
            overlay.AddRect(left, top, width, 2, Vector4.Zero, color, stableId, Hash(left, top, width));
            overlay.AddRect(left, bottom, width, 2, Vector4.Zero, color, stableId + 1, Hash(left, bottom, width));
            overlay.AddRect(left, top, 2, height, Vector4.Zero, color, stableId + 2, Hash(left, top, height));
            overlay.AddRect(right, top, 2, height, Vector4.Zero, color, stableId + 3, Hash(right, top, height));
        }

        private static bool TryWorldToMap(in WorldAabbCm bounds, int mapX, int mapY, float worldXcm, float worldYcm, out int x, out int y)
        {
            float nx = (worldXcm - bounds.Left) / MathF.Max(1f, bounds.Width);
            float ny = (worldYcm - bounds.Top) / MathF.Max(1f, bounds.Height);
            if (!float.IsFinite(nx) || !float.IsFinite(ny))
            {
                x = 0;
                y = 0;
                return false;
            }

            x = mapX + (int)MathF.Round(Math.Clamp(nx, 0f, 1f) * (MapSize - 1));
            y = mapY + (int)MathF.Round((1f - Math.Clamp(ny, 0f, 1f)) * (MapSize - 1));
            return true;
        }

        private static BoardConfig? ResolveBoardConfig(GameEngine engine, IBoard? board)
        {
            if (board == null || engine.CurrentMapSession?.MapConfig.Boards == null)
            {
                return null;
            }

            for (int i = 0; i < engine.CurrentMapSession.MapConfig.Boards.Count; i++)
            {
                BoardConfig candidate = engine.CurrentMapSession.MapConfig.Boards[i];
                if (string.Equals(candidate.Name, board.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static int ResolveChunkSizeCm(IBoard? board, BoardConfig? boardConfig, WorldSizeSpec worldSize)
        {
            if (board?.LoadedChunks is WorldGridLoadedChunks worldGridLoadedChunks)
            {
                return Math.Max(1, worldGridLoadedChunks.ChunkSizeCm);
            }

            int cellSizeCm = boardConfig?.GridCellSizeCm > 0 ? boardConfig.GridCellSizeCm : Math.Max(1, worldSize.GridCellSizeCm);
            int chunkSizeCells = boardConfig?.ChunkSizeCells > 0 ? boardConfig.ChunkSizeCells : 64;
            return Math.Max(1, cellSizeCm * chunkSizeCells);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder != 0 && ((remainder < 0) != (divisor < 0)) ? quotient - 1 : quotient;
        }

        private static int Hash<TA, TB>(TA a, TB b) => HashCode.Combine(a, b);

        private static int Hash<TA, TB, TC>(TA a, TB b, TC c) => HashCode.Combine(a, b, c);
    }
}
