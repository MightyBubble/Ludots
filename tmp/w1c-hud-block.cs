        private static void DrawLightweightDiagnosticHud(GameEngine engine, PresentationTimingDiagnostics? timing)
        {
            if (timing == null)
            {
                return;
            }

            ScreenHudBatchBuffer? screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            WorldHudBatchBuffer? worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            Ludots.Core.Gameplay.GAS.EffectRequestQueue? effectRequests = engine.GetService(CoreServiceKeys.EffectRequestQueue);
            float frameMs = timing.LastWallFrameMs > 0.001f
                ? timing.LastWallFrameMs
                : (timing.WallFrameMs > 0.001f ? timing.WallFrameMs : timing.LastFrameMs);
            float fps = frameMs > 0.001f ? 1000f / frameMs : 0f;
            string line1 = $"FPS {FormatFixed(fps, 4, 0)}  FRAME {FormatFixed(frameMs, 5, 1)}MS  TICK {FormatFixed(timing.LastTotalTickMs, 5, 1)}MS";
            string line2 = $"ISM {FormatFixed(timing.PrimitiveInstancesLastFrame, 6)}  FIELD {FormatFixed(timing.GlobalFieldTexturesLastFrame, 4)}/{FormatFixed(timing.GlobalFieldDirtyUploadsLastFrame, 4)}  3D {FormatFixed(timing.LastMode3DMs, 5, 1)}MS";
            string line3 = $"HUD {FormatFixed(timing.WorldHudProjectedLastFrame, 6)}/{FormatFixed(worldHud?.Count ?? 0, 6)}  BAR {FormatFixed(screenHud?.BarCount ?? 0, 6)}  TEXT {FormatFixed(screenHud?.TextCount ?? 0, 6)}";
            string line4 = $"SKIA {FormatFixed(timing.LastScreenOverlayPaintMs, 5, 1)}MS  EMIT {FormatFixed(timing.LastPresenterEmitMs, 5, 1)}MS  BEHAV {FormatFixed(timing.LastPresenterBehaviorMs, 5, 1)}MS";
            string line5 = $"FXQ {FormatFixed(effectRequests?.Count ?? 0, 6)}  OVF {FormatFixed(effectRequests?.OverflowCount ?? 0, 6)}  AVL {FormatFixed(effectRequests?.AvailableCapacity ?? 0, 6)}";

            const int x = 10;
            const int y = 10;
            const int fontSize = 20;
            const int lineHeight = 25;
            const int panelWidth = 720;
            const int panelHeight = 137;
            var background = new Color(0, 0, 0, 238);
            var border = new Color(80, 255, 150, 255);
            Rl.DrawRectangle(x - 8, y - 8, panelWidth, panelHeight, background);
            Rl.DrawRectangleLines(x - 8, y - 8, panelWidth, panelHeight, border);
            DrawDiagnosticText(line1, x, y, fontSize, new Color(215, 255, 220, 255));
            DrawDiagnosticText(line2, x, y + lineHeight, fontSize, new Color(220, 240, 255, 255));
            DrawDiagnosticText(line3, x, y + lineHeight * 2, fontSize, new Color(255, 245, 185, 255));
            DrawDiagnosticText(line4, x, y + lineHeight * 3, fontSize, new Color(245, 210, 255, 255));
            DrawDiagnosticText(line5, x, y + lineHeight * 4, fontSize, new Color(255, 215, 180, 255));
        }

        private static string FormatFixed(float value, int width, int decimals)
        {
            string text = decimals <= 0 ? value.ToString("F0") : value.ToString($"F{decimals}");
            return text.Length >= width ? text.Substring(text.Length - width, width) : text.PadLeft(width);
        }

        private static string FormatFixed(double value, int width, int decimals)
        {
            string text = decimals <= 0 ? value.ToString("F0") : value.ToString($"F{decimals}");
            return text.Length >= width ? text.Substring(text.Length - width, width) : text.PadLeft(width);
        }

        private static string FormatFixed(int value, int width)
        {
            string text = value.ToString();
            return text.Length >= width ? text.Substring(text.Length - width, width) : text.PadLeft(width);
        }

        private static void DrawDiagnosticText(string text, int x, int y, int fontSize, Color color)
        {
            _ = fontSize;
            DrawBitmapText(text, x + 2, y + 2, 2, new Color(0, 0, 0, 255));
            DrawBitmapText(text, x, y, 2, color);
        }

        private static void DrawBitmapText(string text, int x, int y, int scale, Color color)
        {
            int cursor = x;
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToUpperInvariant(text[i]);
                if (c == ' ')
                {
                    cursor += 4 * scale;
                    continue;
                }

                ulong glyph = GetDiagnosticGlyph(c);
                for (int row = 0; row < 7; row++)
                {
                    int bits = (int)((glyph >> ((6 - row) * 5)) & 0b11111UL);
                    for (int col = 0; col < 5; col++)
                    {
                        if ((bits & (1 << (4 - col))) == 0)
                        {
                            continue;
                        }

                        Rl.DrawRectangle(cursor + col * scale, y + row * scale, scale, scale, color);
                    }
                }

                cursor += 6 * scale;
            }
        }

        private static ulong PackDiagnosticGlyph(int r0, int r1, int r2, int r3, int r4, int r5, int r6)
        {
            return (((ulong)r0 & 0b11111UL) << 30) |
                   (((ulong)r1 & 0b11111UL) << 25) |
                   (((ulong)r2 & 0b11111UL) << 20) |
                   (((ulong)r3 & 0b11111UL) << 15) |
                   (((ulong)r4 & 0b11111UL) << 10) |
                   (((ulong)r5 & 0b11111UL) << 5) |
                   ((ulong)r6 & 0b11111UL);
        }

        private static ulong GetDiagnosticGlyph(char c)
        {
            return c switch
            {
                'A' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
                'B' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110),
                'C' => PackDiagnosticGlyph(0b01111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b01111),
                'D' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110),
                'E' => PackDiagnosticGlyph(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111),
                'F' => PackDiagnosticGlyph(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000),
                'G' => PackDiagnosticGlyph(0b01111, 0b10000, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111),
                'H' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
                'I' => PackDiagnosticGlyph(0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b11111),
                'K' => PackDiagnosticGlyph(0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001),
                'L' => PackDiagnosticGlyph(0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111),
                'M' => PackDiagnosticGlyph(0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001),
                'N' => PackDiagnosticGlyph(0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001),
                'O' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
                'P' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000),
                'R' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001),
                'S' => PackDiagnosticGlyph(0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110),
                'T' => PackDiagnosticGlyph(0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100),
                'U' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
                'V' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b01010, 0b00100),
                'W' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010),
                'X' => PackDiagnosticGlyph(0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001),
                'Y' => PackDiagnosticGlyph(0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100),
                '0' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110),
                '1' => PackDiagnosticGlyph(0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110),
                '2' => PackDiagnosticGlyph(0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111),
                '3' => PackDiagnosticGlyph(0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110),
                '4' => PackDiagnosticGlyph(0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010),
                '5' => PackDiagnosticGlyph(0b11111, 0b10000, 0b10000, 0b11110, 0b00001, 0b00001, 0b11110),
                '6' => PackDiagnosticGlyph(0b01110, 0b10000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110),
                '7' => PackDiagnosticGlyph(0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000),
                '8' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110),
                '9' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00001, 0b01110),
                '.' => PackDiagnosticGlyph(0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b01100, 0b01100),
                '/' => PackDiagnosticGlyph(0b00001, 0b00010, 0b00010, 0b00100, 0b01000, 0b01000, 0b10000),
                '-' => PackDiagnosticGlyph(0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000),
                ':' => PackDiagnosticGlyph(0b00000, 0b01100, 0b01100, 0b00000, 0b01100, 0b01100, 0b00000),
                '|' => PackDiagnosticGlyph(0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100),
                _ => PackDiagnosticGlyph(0b11111, 0b10001, 0b00001, 0b00110, 0b00100, 0b00000, 0b00100),
            };
        }
        private static string BuildTimingDiagnostic(
            GameEngine engine,
            PresentationTimingDiagnostics? timing,
            PresentationOverlayScene? overlayScene)
        {
            if (timing == null)
            {
                return "timing unavailable";
            }

            WorldHudBatchBuffer? worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            ScreenHudBatchBuffer? screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            PrimitiveDrawBuffer? primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            PresenterEntityRuntime? presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime);
            GroundOverlayBuffer? groundOverlay = engine.GetService(CoreServiceKeys.GroundOverlayBuffer);
            SplineRibbonBuffer? splineRibbon = engine.GetService(CoreServiceKeys.SplineRibbonBuffer);
            DebugDrawCommandBuffer? debugDraw = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer);
            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            IBenchmarkSceneController? benchmarkController = engine.GetService(CoreServiceKeys.BenchmarkSceneController);
            bool cleanPerformanceMode = IsCleanPerformanceScene(benchmarkController);
            float frameMs = timing.LastWallFrameMs > 0.001f
                ? timing.LastWallFrameMs
                : (timing.WallFrameMs > 0.001f ? timing.WallFrameMs : timing.LastFrameMs);
            float fps = frameMs > 0.001f ? 1000f / frameMs : 0f;
            int rawDebugDrawCount = debugDraw == null ? 0 : debugDraw.Lines.Count + debugDraw.Circles.Count + debugDraw.Boxes.Count;
            string presenterDefs = presenters?.BuildActiveDefinitionSummary(8) ?? string.Empty;
            return $"timing frame={frameMs:F2}ms fps={fps:F1} cleanPerf={(cleanPerformanceMode ? 1 : 0)} visibleEntities={timing.VisibleEntitiesLastFrame} presenterActive={presenters?.ActiveCount ?? 0} presenterDefs={presenterDefs} primitiveRaw={primitives?.Count ?? 0} primitiveStaticRaw={primitives?.StaticMeshLaneItemCount ?? 0} skinnedRaw={timing.SkinnedRawLastFrame} gpuSkinned={timing.GpuSkinnedInstancesLastFrame}/{timing.GpuSkinnedBatchesLastFrame} gpuSkinBuild={timing.LastGpuSkinnedMatrixBuildMs:F2} gpuSkinDraw={timing.LastGpuSkinnedMeshDrawMs:F2} gap={timing.LastHostLoopGapMs:F2} poll={timing.LastWindowPollMs:F2} pre={timing.LastHostPreTickMs:F2} tick={timing.LastTotalTickMs:F2} post={timing.LastHostPostTickMs:F2} begin={timing.LastBeginDrawingMs:F2} sim={timing.LastSimulationMs:F2} simTop1={timing.LastSimulationTopSystem1Name}:{timing.LastSimulationTopSystem1Ms:F2} simTop2={timing.LastSimulationTopSystem2Name}:{timing.LastSimulationTopSystem2Ms:F2} simTop3={timing.LastSimulationTopSystem3Name}:{timing.LastSimulationTopSystem3Ms:F2} presentation={timing.LastPresentationMs:F2} presTop1={timing.LastPresentationTopSystem1Name}:{timing.LastPresentationTopSystem1Ms:F2} presTop2={timing.LastPresentationTopSystem2Name}:{timing.LastPresentationTopSystem2Ms:F2} presTop3={timing.LastPresentationTopSystem3Name}:{timing.LastPresentationTopSystem3Ms:F2} behavior={timing.LastPresenterBehaviorMs:F2} behaviorBoot={timing.PresenterBootstrapCountLastFrame} behaviorOwner={timing.PresenterOwnerChangesLastFrame} behaviorAttr={timing.PresenterOwnerAttributeChangesLastFrame} behaviorTag={timing.PresenterOwnerTagChangesLastFrame} behaviorTick={timing.PresenterTickDrivenCountLastFrame} animator={timing.LastPresenterAnimatorMs:F2} transformSync={timing.LastPresenterEntityTransformSyncMs:F2} minimapCollect={timing.LastPresenterMinimapMarkerMs:F2} minimapMarkers={timing.PresenterMinimapMarkersLastFrame}/{timing.PresenterMinimapDroppedLastFrame} minimapProject={timing.LastMinimapProjectionMs:F2} minimapScreen={timing.MinimapScreenMarkersLastFrame}/{timing.MinimapScreenMarkersDroppedLastFrame} heightSync={timing.LastTerrainHeightSyncMs:F2} heightSamples={timing.TerrainHeightSamplesLastFrame} requestFlush={timing.LastPresentationRequestFlushMs:F2} spawnBatch={timing.LastRuntimeSpawnBatchPrepareMs:F2}/{timing.LastRuntimeSpawnWorldCreateMs:F2}/{timing.LastRuntimeSpawnFillBatchMs:F2}/{timing.LastRuntimeSpawnPostSpawnMs:F2} spawnPerf={timing.LastRuntimeSpawnPresenterBatchMs:F2}/{timing.LastRuntimeSpawnPresenterCreateMs:F2}/{timing.LastRuntimeSpawnPresenterBootstrapMarkMs:F2} spawnPerfParts={timing.LastRuntimeSpawnPresenterCreateSetupMs:F2}/{timing.LastRuntimeSpawnPresenterWorldCreateMs:F2}/{timing.LastRuntimeSpawnPresenterComponentFillMs:F2}/{timing.LastRuntimeSpawnPresenterIndexWriteMs:F2}/{timing.LastRuntimeSpawnPresenterOwnerPayloadMs:F2}/{timing.LastRuntimeSpawnPresenterPostCreateMs:F2} spawnPerfChildParts={timing.LastRuntimeSpawnPresenterChildSetupMs:F2}/{timing.LastRuntimeSpawnPresenterChildWorldCreateMs:F2}/{timing.LastRuntimeSpawnPresenterChildComponentFillMs:F2}/{timing.LastRuntimeSpawnPresenterChildIndexWriteMs:F2}/{timing.LastRuntimeSpawnPresenterChildStableIdMs:F2} cull={timing.LastCameraCullingMs:F2} cullSpatial={timing.LastCameraCullingSpatialQueryMs:F2} cullStatic={timing.LastCameraCullingStaticProcessMs:F2} cullDyn={timing.LastCameraCullingDynamicProcessMs:F2} cullEntity={timing.LastCameraCullingEntityProcessMs:F2} cullSync={timing.LastCameraCullingPresenterSyncMs:F2} hudProj={timing.LastWorldHudProjectionMs:F2} hudRaw={timing.WorldHudItemsLastProjection} hudProjected={timing.WorldHudProjectedLastFrame} hudDensitySkip={timing.WorldHudDensitySkippedLastFrame} mode3D={timing.LastMode3DMs:F2} terrain={timing.LastTerrainRenderMs:F2} terrainChunks={timing.TerrainChunksDrawnLastFrame}/{timing.TerrainChunksBuiltLastFrame} field={timing.LastGlobalFieldRenderMs:F2} fieldCount={timing.GlobalFieldTexturesLastFrame} fieldDirty={timing.GlobalFieldDirtyUploadsLastFrame} fieldArea={timing.GlobalFieldDirtyUploadAreaLastFrame} fieldDraws={timing.GlobalFieldDrawsLastFrame} primitive={timing.LastPrimitiveRenderMs:F2} primSync={timing.LastPrimitivePersistentSyncMs:F2} primBucket={timing.LastPrimitivePersistentBucketDrawMs:F2} primImmediate={timing.LastPrimitiveImmediateDrawMs:F2} primImmediateSkip={timing.PrimitiveImmediateSkippedLastFrame} primBuild={timing.LastPrimitiveMatrixBuildMs:F2} primDraw={timing.LastPrimitiveMeshDrawMs:F2} primInstances={timing.PrimitiveInstancesLastFrame} primBatches={timing.PrimitiveBatchesLastFrame} primCache={timing.PrimitiveMatrixCacheHitsLastFrame}/{timing.PrimitiveMatrixCacheMissesLastFrame} ground={timing.LastGroundOverlayRenderMs:F2} groundCount={timing.GroundOverlaysLastFrame} groundRaw={groundOverlay?.Count ?? 0} spline={timing.LastSplineRibbonRenderMs:F2} splineCount={timing.SplineRibbonsLastFrame} splineRaw={splineRibbon?.Count ?? 0} debugDraw={timing.LastDebugDrawRenderMs:F2} debugDrawCount={timing.DebugDrawCommandsLastFrame} debugDrawRaw={rawDebugDrawCount} overlay={timing.LastScreenOverlayDrawMs:F2} overlayBuild={timing.LastScreenOverlayBuildMs:F2} overlayDirtyLanes={timing.ScreenOverlayDirtyLanesLastFrame} overlayItems={timing.ScreenOverlayItemsLastFrame} overlayRebuilt={timing.ScreenOverlayRebuiltLanesLastFrame} overlayPaint={timing.LastScreenOverlayPaintMs:F2} overlayComposite={timing.LastScreenOverlayCompositeMs:F2} uiRender={timing.LastUiRenderMs:F2} uiUpload={timing.LastUiUploadMs:F2} overlayFinal={timing.LastScreenOverlayFinalDrawMs:F2} nativeDiag={timing.LastNativeDiagnosticHudMs:F2} emit={timing.LastPresenterEmitMs:F2} emitDirty={timing.LastPresenterEmitDirtyProcessMs:F2} emitDirtyCount={timing.PresenterEmitDirtyCountLastFrame} emitRetained={timing.LastPresenterEmitRetainedProcessMs:F2} emitRetainedCount={timing.PresenterEmitRetainedCountLastFrame} emitRetainedDirectPath={timing.PresenterEmitRetainedDirectHitsLastFrame}/{timing.PresenterEmitRetainedFullPathLastFrame}/{timing.PresenterEmitRetainedDirectMissesLastFrame} endDraw={timing.LastEndDrawingMs:F2} screenshot={timing.LastScreenshotMs:F2} worldHud={worldHud?.Count ?? 0} screenBars={screenHud?.BarCount ?? 0} screenText={screenHud?.TextCount ?? 0} worldHudDrops={worldHud?.DroppedTotal ?? 0} screenHudDrops={screenHud?.DroppedTotal ?? 0} overlaySceneDrops={overlayScene?.DroppedTotal ?? 0}";
        }

        private static void ValidateRequiredContextBeforeLoop(GameEngine engine)
        {
            ValidateKey(engine, CoreServiceKeys.ScreenProjector);
            ValidateKey(engine, CoreServiceKeys.ScreenRayProvider);
            ValidateKey(engine, CoreServiceKeys.RenderDebugState);
        }

        private static void ValidateKey<T>(GameEngine engine, ServiceKey<T> key)
        {
            if (!engine.TryGetService(key, out _))
            {
                throw new InvalidOperationException($"Required service missing or invalid: {key.Name} expected {typeof(T).FullName}");
            }
        }

