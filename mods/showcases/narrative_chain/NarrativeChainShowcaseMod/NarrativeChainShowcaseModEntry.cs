using System;
using System.IO;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NarrativeChainShowcaseMod.Runtime;

namespace NarrativeChainShowcaseMod
{
    public sealed class NarrativeChainShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Log("[NarrativeChainShowcaseMod] Loaded");
            string hudManifestPath = ResolveHudManifestPath(context);
            var runtime = new NarrativeChainRuntime(hudManifestPath);

            context.OnEvent(GameEvents.GameStart, runtime.HandleGameStartAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapLoadedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapLoadedAsync);
            context.OnEvent(NarrativeEventKeys.CinematicStepEntered, runtime.HandleCinematicStepEnteredAsync);
            context.OnEvent(NarrativeEventKeys.CinematicCompleted, runtime.HandleCinematicCompletedAsync);
            context.OnEvent(TaskEventKeys.Activated, runtime.HandleTaskActivatedAsync);
            context.OnEvent(TaskEventKeys.Completed, runtime.HandleTaskCompletedAsync);
            context.OnEvent(TaskEventKeys.Signal, runtime.HandleTaskSignalAsync);
        }

        public void OnUnload()
        {
        }

        private static string ResolveHudManifestPath(IModContext context)
        {
            if (!context.VFS.TryResolveFullPath(NarrativeChainIds.HudManifestResourceUri, out string? fullPath) ||
                !File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Chain HUD manifest '{NarrativeChainIds.HudManifestResourceUri}' was not found in the mod mount.",
                    NarrativeChainIds.HudManifestResourceUri);
            }

            return fullPath;
        }
    }
}
