using System;
using System.Collections.Generic;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Compiles semantic presenter parameter names into opaque runtime integer keys.
    /// Runtime systems keep using integers; authoring should prefer these names over
    /// cross-file numeric conventions.
    /// </summary>
    public static class PresenterParamKeyRegistry
    {
        public const int UnsetParamKey = -1;
        private const int CustomStartId = 200_000;
        private static readonly object Sync = new();
        private static readonly Dictionary<string, int> WellKnown = CreateWellKnown();
        private static readonly Dictionary<int, string> WellKnownNames = CreateWellKnownNames();
        private static StringIntRegistry _custom = CreateCustomRegistry();

        public static int Register(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Presenter param key must not be null or whitespace.", nameof(key));
            }

            if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Presenter param key must not include leading or trailing whitespace.", nameof(key));
            }

            if (WellKnown.TryGetValue(key, out int wellKnownId))
            {
                return wellKnownId;
            }

            lock (Sync)
            {
                return _custom.Register(key);
            }
        }

        public static bool TryGetId(string key, out int id)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                id = 0;
                return false;
            }

            if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
            {
                id = 0;
                return false;
            }

            if (WellKnown.TryGetValue(key, out id))
            {
                return true;
            }

            lock (Sync)
            {
                return _custom.TryGetId(key, out id);
            }
        }

        public static string GetName(int id)
        {
            if (WellKnownNames.TryGetValue(id, out string? wellKnownName))
            {
                return wellKnownName;
            }

            lock (Sync)
            {
                return _custom.GetName(id);
            }
        }

        public static RegistryMapping[] SnapshotMappings()
        {
            lock (Sync)
            {
                return RegistryMappingSnapshot.Merge(
                    RegistryMappingSnapshot.FromNameToId(WellKnown),
                    _custom.SnapshotMappings());
            }
        }

        public static void ClearCustomKeysForTests()
        {
            lock (Sync)
            {
                _custom = CreateCustomRegistry();
            }
        }

        private static StringIntRegistry CreateCustomRegistry()
        {
            return new StringIntRegistry(
                capacity: 256,
                startId: CustomStartId,
                invalidId: UnsetParamKey,
                comparer: StringComparer.Ordinal);
        }

        private static Dictionary<string, int> CreateWellKnown()
        {
            return new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["worldBar.fillRatio"] = WellKnownPresenterParamKeys.BarFillRatio,
                ["worldBar.width"] = WellKnownPresenterParamKeys.BarWidth,
                ["worldBar.height"] = WellKnownPresenterParamKeys.BarHeight,
                ["worldBar.foreground.r"] = WellKnownPresenterParamKeys.BarForegroundR,
                ["worldBar.foreground.g"] = WellKnownPresenterParamKeys.BarForegroundG,
                ["worldBar.foreground.b"] = WellKnownPresenterParamKeys.BarForegroundB,
                ["worldBar.foreground.a"] = WellKnownPresenterParamKeys.BarForegroundA,
                ["worldBar.background.r"] = WellKnownPresenterParamKeys.BarBackgroundR,
                ["worldBar.background.g"] = WellKnownPresenterParamKeys.BarBackgroundG,
                ["worldBar.background.b"] = WellKnownPresenterParamKeys.BarBackgroundB,
                ["worldBar.background.a"] = WellKnownPresenterParamKeys.BarBackgroundA,
                ["worldText.value0"] = WellKnownPresenterParamKeys.TextValue0,
                ["worldText.value1"] = WellKnownPresenterParamKeys.TextValue1,
                ["worldText.fontSize"] = WellKnownPresenterParamKeys.TextFontSize,
                ["worldText.color.r"] = WellKnownPresenterParamKeys.TextColorR,
                ["worldText.color.g"] = WellKnownPresenterParamKeys.TextColorG,
                ["worldText.color.b"] = WellKnownPresenterParamKeys.TextColorB,
                ["worldText.color.a"] = WellKnownPresenterParamKeys.TextColorA,
                ["worldText.tokenId"] = WellKnownPresenterParamKeys.TextTokenId,
                ["groundOverlay.radius"] = WellKnownPresenterParamKeys.OverlayRadius,
                ["groundOverlay.innerRadius"] = WellKnownPresenterParamKeys.OverlayInnerRadius,
                ["groundOverlay.angle"] = WellKnownPresenterParamKeys.OverlayAngle,
                ["groundOverlay.rotation"] = WellKnownPresenterParamKeys.OverlayRotation,
                ["groundOverlay.fill.r"] = WellKnownPresenterParamKeys.OverlayFillR,
                ["groundOverlay.fill.g"] = WellKnownPresenterParamKeys.OverlayFillG,
                ["groundOverlay.fill.b"] = WellKnownPresenterParamKeys.OverlayFillB,
                ["groundOverlay.fill.a"] = WellKnownPresenterParamKeys.OverlayFillA,
                ["groundOverlay.border.r"] = WellKnownPresenterParamKeys.OverlayBorderR,
                ["groundOverlay.border.g"] = WellKnownPresenterParamKeys.OverlayBorderG,
                ["groundOverlay.border.b"] = WellKnownPresenterParamKeys.OverlayBorderB,
                ["groundOverlay.border.a"] = WellKnownPresenterParamKeys.OverlayBorderA,
                ["groundOverlay.border.width"] = WellKnownPresenterParamKeys.OverlayBorderWidth,
                ["groundOverlay.length"] = WellKnownPresenterParamKeys.OverlayLength,
                ["groundOverlay.width"] = WellKnownPresenterParamKeys.OverlayWidth,
                ["worldSpline.p0"] = WellKnownPresenterParamKeys.SplineP0,
                ["worldSpline.p1"] = WellKnownPresenterParamKeys.SplineP1,
                ["worldSpline.p2"] = WellKnownPresenterParamKeys.SplineP2,
                ["worldSpline.p3"] = WellKnownPresenterParamKeys.SplineP3,
                ["worldSpline.width"] = WellKnownPresenterParamKeys.SplineWidth,
                ["worldSpline.fill"] = WellKnownPresenterParamKeys.SplineFillColor,
                ["worldSpline.border"] = WellKnownPresenterParamKeys.SplineBorderColor,
                ["worldSpline.border.width"] = WellKnownPresenterParamKeys.SplineBorderWidth,
                ["marker3d.scale"] = WellKnownPresenterParamKeys.MarkerScale,
                ["marker3d.scale.x"] = WellKnownPresenterParamKeys.MarkerScaleX,
                ["marker3d.scale.y"] = WellKnownPresenterParamKeys.MarkerScaleY,
                ["marker3d.scale.z"] = WellKnownPresenterParamKeys.MarkerScaleZ,
                ["marker3d.color.r"] = WellKnownPresenterParamKeys.MarkerColorR,
                ["marker3d.color.g"] = WellKnownPresenterParamKeys.MarkerColorG,
                ["marker3d.color.b"] = WellKnownPresenterParamKeys.MarkerColorB,
                ["marker3d.color.a"] = WellKnownPresenterParamKeys.MarkerColorA,
            };
        }

        private static Dictionary<int, string> CreateWellKnownNames()
        {
            var names = new Dictionary<int, string>();
            foreach (var kvp in WellKnown)
            {
                names[kvp.Value] = kvp.Key;
            }

            return names;
        }
    }
}
