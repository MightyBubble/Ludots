using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.MassNavigation.Presentation;

public readonly record struct MassNavigationPresentationProxyItem(
    Vector3 Position,
    Vector3 Scale,
    Vector4 Color);

public readonly record struct MassNavigationPresentationProxyStats(
    int AgentCount,
    int MarkerCount,
    int Written,
    int Dropped);

public static class MassNavigationPresentationProxyCollector
{
    private static readonly QueryDescription AgentQuery = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, WorldPositionCm>()
        .WithNone<PresentationDestroyPending, SuspendedTag>();

    private static readonly QueryDescription HotspotMarkerQuery = new QueryDescription()
        .WithAll<MassNavigationHotspotMarker, WorldPositionCm>()
        .WithNone<PresentationDestroyPending, SuspendedTag>();

    public static MassNavigationPresentationProxyStats Collect(
        World world,
        Span<MassNavigationPresentationProxyItem> destination)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        int agentCount = 0;
        int markerCount = 0;
        int written = 0;
        int dropped = 0;

        foreach (ref var chunk in world.Query(in AgentQuery))
        {
            Span<MassNavigationAgent> agents = chunk.GetSpan<MassNavigationAgent>();
            Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
            bool hasProfiles = chunk.Has<MassNavigationAgentProfile>();
            bool hasVisuals = chunk.Has<VisualTransform>();
            Span<MassNavigationAgentProfile> profiles = hasProfiles ? chunk.GetSpan<MassNavigationAgentProfile>() : default;
            Span<VisualTransform> visuals = hasVisuals ? chunk.GetSpan<VisualTransform>() : default;

            foreach (int index in chunk)
            {
                agentCount++;
                MassNavigationAgent agent = agents[index];
                int profileId = hasProfiles ? profiles[index].ProfileId : agent.ProfileId;
                bool heavy = hasProfiles && profiles[index].Heavy;
                float visualScale = hasProfiles ? profiles[index].VisualScale : 1f;

                Vector3 scale = ResolveAgentScale(heavy, visualScale);
                Vector3 position = hasVisuals && IsFinite(visuals[index].Position)
                    ? visuals[index].Position
                    : WorldPlane2D.LogicCmToVisualMeters(in worldPositions[index].Value);
                position.Y += scale.Y * 0.5f;

                if (!TryAppend(
                        destination,
                        ref written,
                        new MassNavigationPresentationProxyItem(
                            position,
                            scale,
                            ResolveAgentColor(profileId, heavy))))
                {
                    dropped++;
                }
            }
        }

        foreach (ref var chunk in world.Query(in HotspotMarkerQuery))
        {
            Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
            bool hasVisuals = chunk.Has<VisualTransform>();
            Span<VisualTransform> visuals = hasVisuals ? chunk.GetSpan<VisualTransform>() : default;

            foreach (int index in chunk)
            {
                markerCount++;
                Vector3 scale = new(1.25f, 0.18f, 1.25f);
                Vector3 position = hasVisuals && IsFinite(visuals[index].Position)
                    ? visuals[index].Position
                    : WorldPlane2D.LogicCmToVisualMeters(in worldPositions[index].Value);
                position.Y += scale.Y * 0.5f;

                if (!TryAppend(
                        destination,
                        ref written,
                        new MassNavigationPresentationProxyItem(
                            position,
                            scale,
                            new Vector4(1f, 0.78f, 0.18f, 1f))))
                {
                    dropped++;
                }
            }
        }

        return new MassNavigationPresentationProxyStats(agentCount, markerCount, written, dropped);
    }

    private static bool TryAppend(
        Span<MassNavigationPresentationProxyItem> destination,
        ref int written,
        in MassNavigationPresentationProxyItem item)
    {
        if ((uint)written >= (uint)destination.Length)
        {
            return false;
        }

        destination[written++] = item;
        return true;
    }

    private static Vector3 ResolveAgentScale(bool heavy, float visualScale)
    {
        float resolvedScale = float.IsFinite(visualScale) && visualScale > 0f ? visualScale : 1f;
        float width = heavy ? 0.48f : 0.34f;
        float height = heavy ? 1.05f : 0.72f;
        return new Vector3(width * resolvedScale, height * resolvedScale, width * resolvedScale);
    }

    private static Vector4 ResolveAgentColor(int profileId, bool heavy)
    {
        float hue = Fraction(profileId * 0.61803398875f);
        HsvToRgb(hue, heavy ? 0.74f : 0.62f, heavy ? 0.98f : 0.86f, out float r, out float g, out float b);
        return new Vector4(r, g, b, 1f);
    }

    private static float Fraction(float value)
    {
        return value - MathF.Floor(value);
    }

    private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        float sector = h * 6f;
        int bucket = (int)MathF.Floor(sector);
        float f = sector - bucket;
        float p = v * (1f - s);
        float q = v * (1f - (s * f));
        float t = v * (1f - (s * (1f - f)));

        switch (bucket % 6)
        {
            case 0:
                r = v; g = t; b = p;
                break;
            case 1:
                r = q; g = v; b = p;
                break;
            case 2:
                r = p; g = v; b = t;
                break;
            case 3:
                r = p; g = q; b = v;
                break;
            case 4:
                r = t; g = p; b = v;
                break;
            default:
                r = v; g = p; b = q;
                break;
        }
    }

    private static bool IsFinite(in Vector3 value)
    {
        return float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z);
    }
}
