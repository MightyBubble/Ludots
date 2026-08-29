                        if (globalFieldVisualBuffer != null)
                        {
                            globalFieldVisualBuffer.BeginFrame();
                            if (engine.TryGetService(CoreServiceKeys.VisionFogFieldStore, out FogFieldStore fogFieldsForProjection))
                            {
                                fogFieldProjector.Project(fogFieldsForProjection, globalFieldVisualBuffer);
                            }
                        }

                        if (overlaySceneBuilder != null && overlayScene != null)
                        {
                            long overlayBuildStart = Stopwatch.GetTimestamp();
                            overlaySceneBuilder.Build(overlayScene);
                            presentationTiming?.ObserveScreenOverlayBuild(
                                ElapsedMs(overlayBuildStart),
                                overlayScene.DirtyLaneCount,
                                overlayScene.Count);
                        }
                        else
                        {
                            presentationTiming?.ObserveScreenOverlayBuild(0d, 0, 0);
                        }
                        presentationTiming?.ObserveHostPostTick(ElapsedMs(postTickStart));

                        long beginDrawingStart = Stopwatch.GetTimestamp();
                        Rl.BeginDrawing();
                        presentationTiming?.ObserveBeginDrawing(ElapsedMs(beginDrawingStart));
                        Restore3DDepthState();
                        string? activeMapId = engine.CurrentMapSession?.MapId.Value;
                        skyEnvironment.EnsureActiveForMap(activeMapId);
                        waterPass.EnsureActiveForMap(activeMapId);
                        visualHeightmapRenderer.EnsureAlbedoActiveForMap(activeMapId);
                        Color frameClearColor = skyEnvironment.IsActive
                            ? skyEnvironment.ResolveClearColor()
                            : (activeMapRequestsDeepBackground
                                ? new Raylib_cs.Color(6, 10, 16, 255)
                                : new Raylib_cs.Color(0, 0, 0, 255));

                        var activeCamera = cameraAdapter.Camera;
                        CameraRenderState3D activeCameraState = cameraPresenter.SmoothedRenderState;

                        if (skyEnvironment.HasDayPhase)
                        {
                            frameLighting.SetDayPhase(skyEnvironment.DayPhase01);
                        }
                        else
                        {
                            frameLighting.Evaluate();
                        }

                        terrainRenderer.ApplyFrameLighting(frameLighting);
                        visualHeightmapRenderer.ApplyFrameLighting(frameLighting);
                        primitiveRenderer.ApplyFrameLighting(frameLighting, activeCamera.position);
                        primitiveRenderer.DrawSurfaceWireBoxes = drawDebugDraw;

                        bool waterOnVisualHeightmap = waterPass.IsActive &&
                                                      drawTerrain &&
                                                      drawVisualHeightmap &&
                                                      hasVisualHeightmap;
                        bool waterOnVertexMap = waterPass.IsActive &&
                                                drawTerrain &&
                                                !waterOnVisualHeightmap &&
                                                engine.VertexMap != null;
                        bool waterFboEnabled = waterOnVisualHeightmap || waterOnVertexMap;
                        bool postProcessWorldFrame = !waterFboEnabled;
                        if (postProcessWorldFrame)
                        {
                            environmentRenderer.BeginWorldFrame(lastW, lastH, frameClearColor);
                        }
                        else
                        {
                            Rl.ClearBackground(frameClearColor);
                        }
                        if (waterFboEnabled)
                        {
                            waterPass.EnsureRenderTargets(lastW, lastH);
                            waterPass.Advance(dt);

                            Camera3D reflectionCamera = waterPass.BuildReflectionCamera(in activeCamera);
                            waterPass.BeginReflectionPass(frameClearColor);
                            Restore3DDepthState();
                            BeginCoreMode3D(reflectionCamera, in activeCameraState);
                            Restore3DDepthState();
                            if (skyEnvironment.IsActive)
                            {
                                skyEnvironment.Draw(in reflectionCamera, in activeCameraState);
                                Restore3DDepthState();
                            }

                            if (waterOnVisualHeightmap &&
                                engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? vhReflect) &&
                                vhReflect is IVisualHeightmapRenderSource reflectSource)
                            {
                                visualHeightmapRenderer.AbsoluteColorSeaLevelCm = waterPass.WaterPlaneY * 100f;
                                visualHeightmapRenderer.AbsoluteColorPeakSpanCm = reflectSource.RenderProfile.AbsoluteColorPeakSpanCm;
                                visualHeightmapRenderer.Render(reflectSource, reflectionCamera);
                            }
                            else
                            {
                                terrainRenderer.RenderTerrainOnly(TerrainSourceFor(engine.VertexMap), reflectionCamera);
                            }

                            EndCoreMode3D();
                            waterPass.EndPass();

                            waterPass.BeginRefractionPass(frameClearColor);
                            Restore3DDepthState();
                            BeginCoreMode3D(activeCamera, in activeCameraState);
                            Restore3DDepthState();
                            if (skyEnvironment.IsActive)
                            {
                                skyEnvironment.Draw(in activeCamera, in activeCameraState);
                                Restore3DDepthState();
                            }

                            if (waterOnVisualHeightmap &&
                                engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? vhRefract) &&
                                vhRefract is IVisualHeightmapRenderSource refractSource)
                            {
                                visualHeightmapRenderer.AbsoluteColorSeaLevelCm = waterPass.WaterPlaneY * 100f;
                                visualHeightmapRenderer.AbsoluteColorPeakSpanCm = refractSource.RenderProfile.AbsoluteColorPeakSpanCm;
                                visualHeightmapRenderer.Render(refractSource, activeCamera);
                            }
                            else
                            {
                                terrainRenderer.RenderTerrainOnly(TerrainSourceFor(engine.VertexMap), activeCamera);
                            }

                            EndCoreMode3D();
                            waterPass.EndPass();
                        }

                        long mode3DStart = Stopwatch.GetTimestamp();
                        Restore3DDepthState();
                        BeginCoreMode3D(activeCamera, in activeCameraState);
                        Restore3DDepthState();

                        if (skyEnvironment.IsActive)
                        {
                            skyEnvironment.Draw(in activeCamera, in activeCameraState);
                            Restore3DDepthState();
                        }

                        if (drawDebugDraw &&
                            !(drawVisualHeightmap && hasVisualHeightmap) &&
                            !hostDebugGuidesSuppressed)
                        {
                            DrawInfiniteGrid(activeCamera.target, 300, 1.0f, 10);

                            var target = activeCamera.target;
                            Rl.DrawLine3D(target, target + new Vector3(2.0f, 0, 0), Color.RED);
                            Rl.DrawLine3D(target, target + new Vector3(0, 0, 2.0f), Color.BLUE);
                            Rl.DrawLine3D(target, target + new Vector3(0, 2.0f, 0), Color.GREEN);
                        }

                        if (drawVisualHeightmap &&
                            engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? visualHeightmapForTerrain) &&
                            visualHeightmapForTerrain is IVisualHeightmapRenderSource visualTerrainSource)
                        {
                            long terrainStart = Stopwatch.GetTimestamp();
                            if (waterOnVisualHeightmap)
                            {
                                visualHeightmapRenderer.AbsoluteColorSeaLevelCm = waterPass.WaterPlaneY * 100f;
                                visualHeightmapRenderer.AbsoluteColorPeakSpanCm = visualTerrainSource.RenderProfile.AbsoluteColorPeakSpanCm;
                            }
                            else
                            {
                                visualHeightmapRenderer.AbsoluteColorSeaLevelCm = null;
                            }

                            visualHeightmapRenderer.Render(visualTerrainSource, activeCamera);

                            if (waterOnVisualHeightmap)
                            {
                                terrainRenderer.EnsureWaterShadersReady();
                                terrainRenderer.BindReflectiveWater(waterPass);
                                // Half-extent covers the island board (~1.28km); plane follows camera target XZ.
                                terrainRenderer.DrawReflectiveOceanPlane(
                                    waterPass.WaterPlaneY,
                                    halfExtentMeters: 900f,
                                    in activeCamera);
                            }
                            else
                            {
                                terrainRenderer.ClearReflectiveWater();
                            }

                            presentationTiming?.ObserveTerrain(
                                ElapsedMs(terrainStart),
                                visualHeightmapRenderer.ChunkBuildMsLastFrame,
                                visualHeightmapRenderer.DrawnChunkCountLastFrame,
                                visualHeightmapRenderer.BuiltChunkCountLastFrame);
                        }
                        else if (drawTerrain)
                        {
                            long terrainStart = Stopwatch.GetTimestamp();
                            if (waterOnVertexMap)
                            {
                                terrainRenderer.BindReflectiveWater(waterPass);
                            }
                            else
                            {
                                terrainRenderer.ClearReflectiveWater();
                            }

                            terrainRenderer.Render(TerrainSourceFor(engine.VertexMap), activeCamera);
                            presentationTiming?.ObserveTerrain(
                                ElapsedMs(terrainStart),
                                terrainRenderer.ChunkBuildMsLastFrame,
                                terrainRenderer.DrawnChunkCountLastFrame,
                                terrainRenderer.BuiltChunkCountLastFrame);
                        }
                        else
                        {
                            presentationTiming?.ObserveTerrain(0d, 0d, 0, 0);
                        }

                        if (drawNavMeshOverlay)
                        {
                            navMeshPresentationRenderer.Draw(navMeshPresentationBuffer);
                            screenOverlayBuffer?.AddText(
                                10,
                                40,
                                navMeshPresentationBuffer.FormatMetadataLine(),
                                14,
                                new Vector4(1f, 0.92f, 0.5f, 1f));
                        }

                        if (drawFieldOverlays && globalFieldVisualBuffer != null)
                        {
                            long fieldRenderStart = Stopwatch.GetTimestamp();
                            fieldRenderPresenter.Draw(globalFieldVisualBuffer);
                            presentationTiming?.ObserveGlobalFieldRender(
                                ElapsedMs(fieldRenderStart),
                                fieldRenderPresenter.LastFieldTextureCount,
                                fieldRenderPresenter.LastDirtyUploadCount,
                                fieldRenderPresenter.LastDirtyUploadArea,
                                fieldRenderPresenter.LastDrawCount);
                        }
                        else
                        {
                            presentationTiming?.ObserveGlobalFieldRender(0d, 0, 0, 0, 0);
                        }

                        // Benchmark ISM bridge and performer primitive/skinned lanes are independent.
                        // Drawing the benchmark scene must not skip GpuSkinnedInstance / host material / VFX.
                        if (benchmarkRenderer != null)
                        {
                            _ = benchmarkRenderer.Draw(activeCamera);
                        }

                        if (drawPrimitives &&
                            engine.TryGetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, out PrimitiveDrawBuffer draw) &&
                            engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry meshes))
                        {
                            if (!_emptyBufferWarned && draw.GetSpan().Length == 0)
                            {
                                System.Diagnostics.Debug.WriteLine("[RaylibHostLoop] PrimitiveDrawBuffer is empty on first render frame; no Marker3D presenters emitting?");
                                _emptyBufferWarned = true;
                            }
                            long primitiveStart = Stopwatch.GetTimestamp();
                            PrimitiveDrawBuffer? snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer);
                            SkinnedVisualBatchBuffer? skinnedBatch = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
                            engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? visualHeightmap);
                            if (visualHeightmap != null)
                            {
                                visualHeightmapRenderer.BindStampHeightSampleSource(visualHeightmap);
                                terrainRenderer.BindStampHeightSampleSource(visualHeightmap);
                            }

                            primitiveRenderer.Draw(
                                draw,
                                activeCamera,
                                snapshot,
                                skinnedBatch,
                                meshes,
                                renderDebug.AcceptanceScaleMultiplier,
                                visualHeightmap,
                                runtimeStopwatch.Elapsed.TotalSeconds);
                            presentationTiming?.ObservePrimitiveRender(
                                ElapsedMs(primitiveStart),
                                primitiveRenderer.LastInstancedInstances,
                                primitiveRenderer.LastInstancedBatches,
                                primitiveRenderer.LastInstancedMatrixBuildMs,
                                primitiveRenderer.LastInstancedMeshDrawMs,
                                primitiveRenderer.LastInstancedMatrixCacheHits,
                                primitiveRenderer.LastInstancedMatrixCacheMisses,
                                primitiveRenderer.LastPersistentSyncMs,
                                primitiveRenderer.LastPersistentBucketDrawMs,
                                primitiveRenderer.LastImmediateDrawMs,
                                primitiveRenderer.LastImmediateSkippedCount,
                                skinnedBatch?.Count ?? 0,
                                primitiveRenderer.LastGpuSkinnedInstances,
                                primitiveRenderer.LastGpuSkinnedBatches,
                                primitiveRenderer.LastGpuSkinnedMatrixBuildMs,
                                primitiveRenderer.LastGpuSkinnedMeshDrawMs);
                        }
                        else
                        {
                            presentationTiming?.ObservePrimitiveRender(0d, 0, 0);
                        }

                        // Draw ground overlays (range circles, cones, etc.)
                        if (!cleanPerformanceMode &&
                            engine.TryGetService(CoreServiceKeys.GroundOverlayBuffer, out GroundOverlayBuffer overlays) &&
                            overlays.Count > 0)
                        {
                            long groundOverlayStart = Stopwatch.GetTimestamp();
                            RaylibWorldOverlayRenderer.DrawGroundOverlays(overlays);
                            presentationTiming?.ObserveGroundOverlayRender(ElapsedMs(groundOverlayStart), overlays.Count);
                        }
                        else
                        {
                            presentationTiming?.ObserveGroundOverlayRender(0d, 0);
                        }

                        if (!cleanPerformanceMode &&
                            engine.GlobalContext.TryGetValue(CoreServiceKeys.SplineRibbonBuffer.Name, out var splineObj) &&
                            splineObj is SplineRibbonBuffer splineRibbons && splineRibbons.Count > 0)
                        {
                            long splineRibbonStart = Stopwatch.GetTimestamp();
                            RaylibWorldOverlayRenderer.DrawSplineRibbons(splineRibbons);
                            presentationTiming?.ObserveSplineRibbonRender(ElapsedMs(splineRibbonStart), splineRibbons.Count);
                        }
                        else
                        {
                            presentationTiming?.ObserveSplineRibbonRender(0d, 0);
                        }

                        if (drawDebugDraw &&
                            engine.TryGetService(CoreServiceKeys.DebugDrawCommandBuffer, out DebugDrawCommandBuffer dd))
                        {
                            long debugDrawStart = Stopwatch.GetTimestamp();
                            debugDrawRenderer.Draw(dd);
                            presentationTiming?.ObserveDebugDrawRender(
                                ElapsedMs(debugDrawStart),
                                dd.Lines.Count + dd.Circles.Count + dd.Boxes.Count);
                        }
                        else
                        {
                            presentationTiming?.ObserveDebugDrawRender(0d, 0);
                        }

                        EndCoreMode3D();
                        presentationTiming?.ObserveMode3D(ElapsedMs(mode3DStart));
                        if (postProcessWorldFrame)
                        {
                            environmentRenderer.EndWorldFrame(runtimeStopwatch.Elapsed.TotalSeconds);
                        }

                        if (drawSkiaUi)
                        {
                            browserLayerRenderer.Render(uiRoot.Scene, lastW, lastH);
                        }

                        long overlayStart = Stopwatch.GetTimestamp();
                        OverlayCompositeResult overlayResult = overlayCompositor.Render(
                            overlayScene,
                            uiRoot,
                            skiaRenderer,
                            drawSkiaUi,
                            hostDiagnosticUiSuppressed);
                        presentationTiming?.ObserveUiRender(overlayResult.UiRenderMs);
                        presentationTiming?.ObserveUiUpload(overlayResult.UploadMs);
                        presentationTiming?.ObserveCompositeSkip(!overlayResult.RefreshComposite);
                        screenOverlayBuffer?.Clear();
                        presentationTiming?.ObserveScreenOverlayDraw(
                            ElapsedMs(overlayStart),
                            overlayResult.PaintMs,
                            overlayResult.CompositeMs,
                            overlayResult.UploadMs,
                            overlayResult.FinalDrawMs,
                            overlayCompositor.OverlayRenderer.RebuiltLaneCountLastFrame,
                            overlayCompositor.OverlayRenderer.CachedTextLayoutCount);
                        if (timingLogIntervalFrames > 0 && frameIndex % timingLogIntervalFrames == 0)
                        {
                            SkiaOverlayRenderer overlaySkiaRenderer = overlayCompositor.OverlayRenderer;
                            AppendRaylibDiagnostic(
                                diagnosticPath,
                                $"overlay-lanes backend=skia underBar={overlaySkiaRenderer.LastUnderUiBarMs:F2} underText={overlaySkiaRenderer.LastUnderUiTextMs:F2} barBuild={overlaySkiaRenderer.LastBarBatchBuildMs:F2} barDraw={overlaySkiaRenderer.LastBarBatchDrawMs:F2} barBuckets={overlaySkiaRenderer.LastBarBatchBucketCount} barCache={overlaySkiaRenderer.LastBarSpriteCacheHits}/{overlaySkiaRenderer.LastBarSpriteCacheMisses}/clear{overlaySkiaRenderer.LastBarSpriteCacheClears}/size{overlaySkiaRenderer.BarSpriteCacheCount} textBuild={overlaySkiaRenderer.LastTextBatchBuildMs:F2} textDraw={overlaySkiaRenderer.LastTextBatchDrawMs:F2} textBuckets={overlaySkiaRenderer.LastTextSpriteBatchBucketCount} markerBuild={overlaySkiaRenderer.LastMinimapMarkerBatchBuildMs:F2} markerDraw={overlaySkiaRenderer.LastMinimapMarkerBatchDrawMs:F2} markerBuckets={overlaySkiaRenderer.LastMinimapMarkerBatchBucketCount}/{overlaySkiaRenderer.LastMinimapMarkerOrientationBatchBucketCount} markerSpriteCache={overlaySkiaRenderer.LastMinimapMarkerSpriteCacheHits}/{overlaySkiaRenderer.LastMinimapMarkerSpriteCacheMisses}/clear{overlaySkiaRenderer.LastMinimapMarkerSpriteCacheClears}/size{overlaySkiaRenderer.MarkerSpriteCacheCount} textSpriteCache={overlaySkiaRenderer.LastTextSpriteCacheHits}/{overlaySkiaRenderer.LastTextSpriteCacheMisses}/clear{overlaySkiaRenderer.LastTextSpriteCacheClears}/size{overlaySkiaRenderer.TextSpriteCacheCount} textLayout={overlaySkiaRenderer.LastTextLayoutCacheHits}/{overlaySkiaRenderer.LastTextLayoutCacheMisses}/clear{overlaySkiaRenderer.LastTextLayoutCacheClears}/size{overlaySkiaRenderer.CachedTextLayoutCount}");
                        }

                        bool drawLightweightDiagnosticHud = lightweightDiagnosticHudEnabled;
                        if (drawLightweightDiagnosticHud)
                        {
                            long nativeDiagnosticStart = Stopwatch.GetTimestamp();
                            DrawLightweightDiagnosticHud(engine, presentationTiming);
                            presentationTiming?.ObserveNativeDiagnosticHud(ElapsedMs(nativeDiagnosticStart));
                        }
                        else
                        {
                            presentationTiming?.ObserveNativeDiagnosticHud(0d);
                        }

                        long endDrawingStart = Stopwatch.GetTimestamp();
                        Rl.EndDrawing();
                        windowRepaintGuard.AfterPresent();
                        if (frameCapture is RaylibFrameCaptureService frameCaptureDriver)
                        {
                            frameCaptureDriver.OnFramePresented();
                        }
                        presentationTiming?.ObserveEndDrawing(ElapsedMs(endDrawingStart));
                        presentationTiming?.ObserveWallFrame(ElapsedMs(wallFrameStart));
                        previousLoopEnd = Stopwatch.GetTimestamp();

                        frameIndex++;
