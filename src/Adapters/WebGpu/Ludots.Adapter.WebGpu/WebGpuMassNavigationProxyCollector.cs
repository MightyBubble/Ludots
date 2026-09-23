using System;
using Arch.Core;
using Ludots.Client.WebGpu.Rendering;
using Ludots.Core.MassNavigation.Presentation;

namespace Ludots.Adapter.WebGpu
{
    internal static class WebGpuMassNavigationProxyCollector
    {
        public static WebGpuMassNavigationProxyStats Collect(
            World world,
            WebGpuInstanceDraw[] destination,
            int startIndex)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if ((uint)startIndex > (uint)destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            var scratch = new MassNavigationPresentationProxyItem[destination.Length - startIndex];
            MassNavigationPresentationProxyStats stats = MassNavigationPresentationProxyCollector.Collect(world, scratch);
            for (int i = 0; i < stats.Written; i++)
            {
                MassNavigationPresentationProxyItem item = scratch[i];
                destination[startIndex + i] = new WebGpuInstanceDraw
                {
                    Position = item.Position,
                    Scale = item.Scale,
                    Color = item.Color
                };
            }

            return new WebGpuMassNavigationProxyStats(
                stats.AgentCount,
                stats.MarkerCount,
                stats.Written,
                stats.Dropped);
        }
    }

    internal readonly record struct WebGpuMassNavigationProxyStats(
        int AgentCount,
        int MarkerCount,
        int Written,
        int Dropped);
}
