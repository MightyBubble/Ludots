using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input
{
    internal static class ScreenProjectionGrounding
    {
        private static readonly QueryDescription PresentationFrameQuery = new QueryDescription()
            .WithAll<PresentationFrameState, PresentationFrameStateTag>();

        public static ScreenProjectionPoseContext Resolve(
            World world,
            IReadOnlyDictionary<string, object> globals)
        {
            IContinuousHeightmap? heightmap = ResolveHeightmap(globals);
            var readFrame = new ReadPresentationFrameJob();
            world.InlineQuery<ReadPresentationFrameJob, PresentationFrameState>(
                in PresentationFrameQuery,
                ref readFrame);
            if (readFrame.Count != 1)
            {
                throw new InvalidOperationException(
                    $"SPATIAL.ERR.PresentationFrameStateCardinality: count={readFrame.Count}");
            }

            return new ScreenProjectionPoseContext(readFrame.InterpolationAlpha, heightmap);
        }

        private static IContinuousHeightmap? ResolveHeightmap(IReadOnlyDictionary<string, object> globals)
        {
            if (globals.TryGetValue(CoreServiceKeys.ContinuousHeightmap.Name, out object? heightmapObject))
            {
                return heightmapObject as IContinuousHeightmap
                    ?? throw new InvalidOperationException("SPATIAL.ERR.InvalidContinuousHeightmapService");
            }

            if (globals.TryGetValue(CoreServiceKeys.MapSession.Name, out object? sessionObject) &&
                sessionObject is MapSession session &&
                !string.IsNullOrWhiteSpace(MapContinuousHeightmapLoader.ResolveDeclaredAssetPath(session.MapConfig)))
            {
                throw new InvalidOperationException(
                    $"SPATIAL.ERR.DeclaredContinuousHeightmapMissing: map={session.MapId.Value}");
            }

            return null;
        }

        private struct ReadPresentationFrameJob : IForEach<PresentationFrameState>
        {
            public int Count;
            public float InterpolationAlpha;

            public void Update(ref PresentationFrameState state)
            {
                Count++;
                InterpolationAlpha = state.Enabled ? state.InterpolationAlpha : 1f;
            }
        }
    }
}
