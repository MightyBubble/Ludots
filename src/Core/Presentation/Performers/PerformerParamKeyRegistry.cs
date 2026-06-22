using System;
using System.Collections.Generic;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Compiles semantic performer parameter names into opaque runtime integer keys.
    /// Runtime systems keep using integers; authoring should prefer these names over
    /// cross-file numeric conventions.
    /// </summary>
    public static class PerformerParamKeyRegistry
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
                throw new ArgumentException("Performer param key must not be null or whitespace.", nameof(key));
            }

            if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Performer param key must not include leading or trailing whitespace.", nameof(key));
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
                ["worldBar.fillRatio"] = WellKnownPerformerParamKeys.BarFillRatio,
                ["worldBar.width"] = WellKnownPerformerParamKeys.BarWidth,
                ["worldBar.height"] = WellKnownPerformerParamKeys.BarHeight,
                ["worldBar.foreground.r"] = WellKnownPerformerParamKeys.BarForegroundR,
                ["worldBar.foreground.g"] = WellKnownPerformerParamKeys.BarForegroundG,
                ["worldBar.foreground.b"] = WellKnownPerformerParamKeys.BarForegroundB,
                ["worldBar.foreground.a"] = WellKnownPerformerParamKeys.BarForegroundA,
                ["worldBar.background.r"] = WellKnownPerformerParamKeys.BarBackgroundR,
                ["worldBar.background.g"] = WellKnownPerformerParamKeys.BarBackgroundG,
                ["worldBar.background.b"] = WellKnownPerformerParamKeys.BarBackgroundB,
                ["worldBar.background.a"] = WellKnownPerformerParamKeys.BarBackgroundA,
                ["worldText.value0"] = WellKnownPerformerParamKeys.TextValue0,
                ["worldText.value1"] = WellKnownPerformerParamKeys.TextValue1,
                ["worldText.fontSize"] = WellKnownPerformerParamKeys.TextFontSize,
                ["worldText.color.r"] = WellKnownPerformerParamKeys.TextColorR,
                ["worldText.color.g"] = WellKnownPerformerParamKeys.TextColorG,
                ["worldText.color.b"] = WellKnownPerformerParamKeys.TextColorB,
                ["worldText.color.a"] = WellKnownPerformerParamKeys.TextColorA,
                ["worldText.tokenId"] = WellKnownPerformerParamKeys.TextTokenId,
                ["groundOverlay.radius"] = WellKnownPerformerParamKeys.OverlayRadius,
                ["groundOverlay.innerRadius"] = WellKnownPerformerParamKeys.OverlayInnerRadius,
                ["groundOverlay.angle"] = WellKnownPerformerParamKeys.OverlayAngle,
                ["groundOverlay.rotation"] = WellKnownPerformerParamKeys.OverlayRotation,
                ["groundOverlay.fill.r"] = WellKnownPerformerParamKeys.OverlayFillR,
                ["groundOverlay.fill.g"] = WellKnownPerformerParamKeys.OverlayFillG,
                ["groundOverlay.fill.b"] = WellKnownPerformerParamKeys.OverlayFillB,
                ["groundOverlay.fill.a"] = WellKnownPerformerParamKeys.OverlayFillA,
                ["groundOverlay.border.r"] = WellKnownPerformerParamKeys.OverlayBorderR,
                ["groundOverlay.border.g"] = WellKnownPerformerParamKeys.OverlayBorderG,
                ["groundOverlay.border.b"] = WellKnownPerformerParamKeys.OverlayBorderB,
                ["groundOverlay.border.a"] = WellKnownPerformerParamKeys.OverlayBorderA,
                ["groundOverlay.border.width"] = WellKnownPerformerParamKeys.OverlayBorderWidth,
                ["groundOverlay.length"] = WellKnownPerformerParamKeys.OverlayLength,
                ["groundOverlay.width"] = WellKnownPerformerParamKeys.OverlayWidth,
                ["marker3d.scale"] = WellKnownPerformerParamKeys.MarkerScale,
                ["marker3d.scale.x"] = WellKnownPerformerParamKeys.MarkerScaleX,
                ["marker3d.scale.y"] = WellKnownPerformerParamKeys.MarkerScaleY,
                ["marker3d.scale.z"] = WellKnownPerformerParamKeys.MarkerScaleZ,
                ["marker3d.color.r"] = WellKnownPerformerParamKeys.MarkerColorR,
                ["marker3d.color.g"] = WellKnownPerformerParamKeys.MarkerColorG,
                ["marker3d.color.b"] = WellKnownPerformerParamKeys.MarkerColorB,
                ["marker3d.color.a"] = WellKnownPerformerParamKeys.MarkerColorA,
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
