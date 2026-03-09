using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace VirtualCameraShotsMod.Triggers
{
    public sealed class ActivateTaggedVirtualCameraOnMapLoadedTrigger : Trigger
    {
        private const string ShotTagPrefix = "camera.shot:";
        private readonly IModContext _context;

        public ActivateTaggedVirtualCameraOnMapLoadedTrigger(IModContext context)
        {
            _context = context;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var mapTags = context.Get(CoreServiceKeys.MapTags) ?? new List<string>();
            string shotId = ResolveShotId(mapTags);
            if (string.IsNullOrWhiteSpace(shotId))
            {
                return Task.CompletedTask;
            }

            var registry = context.Get(CoreServiceKeys.VirtualCameraRegistry)
                ?? throw new InvalidOperationException("VirtualCameraRegistry is required for VirtualCameraShotsMod.");
            if (!registry.TryGet(shotId, out _))
            {
                throw new InvalidOperationException($"Virtual camera shot '{shotId}' was requested by tag but is not registered.");
            }

            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = shotId
            });

            _context.Log($"[VirtualCameraShotsMod] Activated shot '{shotId}' from map tag.");
            return Task.CompletedTask;
        }

        private static string ResolveShotId(List<string> mapTags)
        {
            for (int i = 0; i < mapTags.Count; i++)
            {
                var tag = mapTags[i];
                if (tag != null && tag.StartsWith(ShotTagPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return tag.Substring(ShotTagPrefix.Length).Trim();
                }
            }

            return string.Empty;
        }
    }
}
