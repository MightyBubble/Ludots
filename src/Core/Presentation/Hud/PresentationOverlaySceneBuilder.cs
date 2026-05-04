using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Minimap;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationOverlaySceneBuilder
    {
        private const int MaxTextPacketCacheEntries = 8192;
        private const int MaxNumericTextCacheEntries = 4096;

        private readonly ScreenHudBatchBuffer _screenHud;
        private readonly WorldHudStringTable? _worldHudStrings;
        private readonly PresentationTextCatalog? _textCatalog;
        private readonly PresentationTextLocaleSelection? _localeSelection;
        private readonly ScreenOverlayBuffer? _screenOverlay;
        private readonly MinimapScreenMarkerBuffer? _minimapMarkers;
        private readonly Dictionary<TextPacketCacheKey, string> _textPacketCache = new();
        private readonly Dictionary<NumericTextCacheKey, string> _numericTextCache = new();
        private readonly Dictionary<int, ScreenHudResolvedTextCacheEntry> _screenHudResolvedTextCache = new();
        private int _lastScreenHudRevision = -1;
        private bool _screenHudBuilt;

        public PresentationOverlaySceneBuilder(
            ScreenHudBatchBuffer screenHud,
            WorldHudStringTable? worldHudStrings,
            PresentationTextCatalog? textCatalog,
            PresentationTextLocaleSelection? localeSelection,
            ScreenOverlayBuffer? screenOverlay,
            MinimapScreenMarkerBuffer? minimapMarkers = null)
        {
            _screenHud = screenHud ?? throw new ArgumentNullException(nameof(screenHud));
            _worldHudStrings = worldHudStrings;
            _textCatalog = textCatalog;
            _localeSelection = localeSelection;
            _screenOverlay = screenOverlay;
            _minimapMarkers = minimapMarkers;
        }

        public void Build(PresentationOverlayScene scene)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            if (TryApplyScreenHudDeltas(scene))
            {
                return;
            }

            bool appendOnlyScreenHud = _screenHud.RequiresFullRebuild &&
                !HasScreenOverlayContent(scene);
            if (appendOnlyScreenHud)
            {
                scene.BeginAppendOnlyBuild();
                AppendScreenHud(scene, appendOnly: true);
            }
            else
            {
                scene.BeginBuild();
                AppendScreenHud(scene, appendOnly: false);
            }

            AppendScreenOverlay(scene);
            if (appendOnlyScreenHud)
            {
                scene.EndAppendOnlyBuild();
            }
            else
            {
                scene.EndBuild();
            }

            _lastScreenHudRevision = _screenHud.ContentRevision;
            _screenHudBuilt = true;
            _screenHud.ClearDeltas();
        }

        private bool TryApplyScreenHudDeltas(PresentationOverlayScene scene)
        {
            if (!_screenHudBuilt)
            {
                return false;
            }

            int screenHudRevision = _screenHud.ContentRevision;
            if (_screenHud.RequiresFullRebuild)
            {
                return false;
            }

            if (screenHudRevision == _lastScreenHudRevision)
            {
                scene.BeginDeltaBuild();
                if (HasScreenOverlayContent(scene))
                {
                    RebuildScreenOverlay(scene);
                }

                _screenHud.ClearDeltas();
                return true;
            }

            ReadOnlySpan<ScreenHudBarItem> dirtyBars = _screenHud.GetDirtyBarSpan();
            ReadOnlySpan<ScreenHudTextItem> dirtyTexts = _screenHud.GetDirtyTextSpan();
            ReadOnlySpan<ScreenHudBarItem> positionOnlyBars = _screenHud.GetPositionOnlyBarSpan();
            ReadOnlySpan<ScreenHudTextItem> positionOnlyTexts = _screenHud.GetPositionOnlyTextSpan();
            ReadOnlySpan<int> removedStableIds = _screenHud.GetRemovedStableIdSpan();
            bool hasPositionOnlyBarRange = _screenHud.HasPositionOnlyBarRange;
            bool hasPositionOnlyTextRange = _screenHud.HasPositionOnlyTextRange;
            if (dirtyBars.Length == 0 &&
                dirtyTexts.Length == 0 &&
                positionOnlyBars.Length == 0 &&
                positionOnlyTexts.Length == 0 &&
                !hasPositionOnlyBarRange &&
                !hasPositionOnlyTextRange &&
                removedStableIds.Length == 0)
            {
                return false;
            }

            scene.BeginDeltaBuild();
            if (ShouldUsePositionOnlyRange(
                _screenHud.PositionOnlyBarCount,
                positionOnlyBars.Length,
                _screenHud.PositionOnlyBarRangeOnly))
            {
                scene.TryUpdateStableBarPositionRange(
                    PresentationOverlayLayer.UnderUi,
                    _screenHud.GetBarSpan(),
                    _screenHud.PositionOnlyBarStart,
                    _screenHud.PositionOnlyBarCount);
            }
            else
            {
                scene.TryUpdateStableBarPositions(PresentationOverlayLayer.UnderUi, positionOnlyBars);
            }

            if (ShouldUsePositionOnlyRange(
                _screenHud.PositionOnlyTextCount,
                positionOnlyTexts.Length,
                _screenHud.PositionOnlyTextRangeOnly))
            {
                scene.TryUpdateStableTextPositionRange(
                    PresentationOverlayLayer.UnderUi,
                    _screenHud.GetTextSpan(),
                    _screenHud.PositionOnlyTextStart,
                    _screenHud.PositionOnlyTextCount);
            }
            else
            {
                scene.TryUpdateStableTextPositions(PresentationOverlayLayer.UnderUi, positionOnlyTexts);
            }

            for (int i = 0; i < removedStableIds.Length; i++)
            {
                int stableId = removedStableIds[i];
                scene.RemoveStable(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar, stableId);
                scene.RemoveStable(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text, stableId);
            }

            for (int i = 0; i < dirtyBars.Length; i++)
            {
                ref readonly ScreenHudBarItem item = ref dirtyBars[i];
                scene.TryUpsertBar(
                    PresentationOverlayLayer.UnderUi,
                    item.ScreenX,
                    item.ScreenY,
                    item.Width,
                    item.Height,
                    item.Value0,
                    item.Color0,
                    item.Color1,
                    item.StableId,
                    item.DirtySerial);
            }

            for (int i = 0; i < dirtyTexts.Length; i++)
            {
                ref readonly ScreenHudTextItem item = ref dirtyTexts[i];
                string? text = ResolveScreenHudText(in item);
                if (!string.IsNullOrEmpty(text))
                {
                    scene.TryUpsertText(
                        PresentationOverlayLayer.UnderUi,
                        item.ScreenX,
                        item.ScreenY,
                        text,
                        item.FontSize <= 0 ? 16 : item.FontSize,
                        item.Color0,
                        item.StableId,
                        item.DirtySerial);
                }
            }

            RebuildScreenOverlay(scene);
            _lastScreenHudRevision = screenHudRevision;
            _screenHud.ClearDeltas();
            return true;
        }

        private static bool ShouldUsePositionOnlyRange(int rangeCount, int sparseCount, bool rangeOnly)
        {
            if (rangeCount <= 0)
            {
                return false;
            }

            if (rangeOnly)
            {
                return true;
            }

            if (sparseCount <= 0)
            {
                return false;
            }

            return sparseCount >= 1024 || rangeCount <= sparseCount * 2;
        }

        private bool HasScreenOverlayContent(PresentationOverlayScene scene)
        {
            return (_screenOverlay != null && _screenOverlay.Count > 0) ||
                (_minimapMarkers != null && _minimapMarkers.Count > 0) ||
                scene.ContainsLayer(PresentationOverlayLayer.TopMost);
        }

        private void RebuildScreenOverlay(PresentationOverlayScene scene)
        {
            if (_screenOverlay == null && (_minimapMarkers == null || _minimapMarkers.Count <= 0))
            {
                if (scene.ContainsLayer(PresentationOverlayLayer.TopMost))
                {
                    scene.ClearLayer(PresentationOverlayLayer.TopMost);
                }

                return;
            }

            scene.BeginLayerBuild(PresentationOverlayLayer.TopMost);
            AppendScreenOverlay(scene);
            scene.EndLayerBuild(PresentationOverlayLayer.TopMost);
        }

        private void AppendScreenHud(PresentationOverlayScene scene, bool appendOnly)
        {
            ReadOnlySpan<ScreenHudBarItem> bars = _screenHud.GetBarSpan();
            for (int i = 0; i < bars.Length; i++)
            {
                ref readonly ScreenHudBarItem item = ref bars[i];
                if (appendOnly)
                {
                    scene.TryAppendBar(
                        PresentationOverlayLayer.UnderUi,
                        item.ScreenX,
                        item.ScreenY,
                        item.Width,
                        item.Height,
                        item.Value0,
                        item.Color0,
                        item.Color1,
                        item.StableId,
                        item.DirtySerial);
                    continue;
                }

                scene.TryAddBar(
                    PresentationOverlayLayer.UnderUi,
                    item.ScreenX,
                    item.ScreenY,
                    item.Width,
                    item.Height,
                    item.Value0,
                    item.Color0,
                    item.Color1,
                    item.StableId,
                    item.DirtySerial);
            }

            ReadOnlySpan<ScreenHudTextItem> texts = _screenHud.GetTextSpan();
            for (int i = 0; i < texts.Length; i++)
            {
                ref readonly ScreenHudTextItem item = ref texts[i];
                string? text = ResolveScreenHudText(in item);
                if (!string.IsNullOrEmpty(text))
                {
                    if (appendOnly)
                    {
                        scene.TryAppendText(
                            PresentationOverlayLayer.UnderUi,
                            item.ScreenX,
                            item.ScreenY,
                            text,
                            item.FontSize <= 0 ? 16 : item.FontSize,
                            item.Color0,
                            item.StableId,
                            item.DirtySerial);
                        continue;
                    }

                    scene.TryAddText(
                        PresentationOverlayLayer.UnderUi,
                        item.ScreenX,
                        item.ScreenY,
                        text,
                        item.FontSize <= 0 ? 16 : item.FontSize,
                        item.Color0,
                        item.StableId,
                        item.DirtySerial);
                }
            }
        }

        private void AppendScreenOverlay(PresentationOverlayScene scene)
        {
            if (_screenOverlay != null)
            {
                ReadOnlySpan<ScreenOverlayItem> span = _screenOverlay.GetSpan();
                for (int i = 0; i < span.Length; i++)
                {
                    ref readonly ScreenOverlayItem item = ref span[i];
                    switch (item.Kind)
                    {
                        case ScreenOverlayItemKind.Text:
                        {
                            string? text = ResolveScreenOverlayText(in item);
                            if (!string.IsNullOrEmpty(text))
                            {
                                scene.TryAddText(
                                    PresentationOverlayLayer.TopMost,
                                    item.X,
                                    item.Y,
                                    text,
                                    item.FontSize <= 0 ? 16 : item.FontSize,
                                    item.Color,
                                    item.StableId,
                                    item.DirtySerial);
                            }

                            break;
                        }

                        case ScreenOverlayItemKind.Rect:
                            scene.TryAddRect(
                                PresentationOverlayLayer.TopMost,
                                item.X,
                                item.Y,
                                item.Width,
                                item.Height,
                                item.BackgroundColor,
                                item.Color,
                                item.StableId,
                                item.DirtySerial);
                            break;

                        case ScreenOverlayItemKind.Line:
                            scene.TryAddLine(
                                PresentationOverlayLayer.TopMost,
                                item.X,
                                item.Y,
                                item.Width,
                                item.Height,
                                item.Thickness,
                                item.Color,
                                item.StableId,
                                item.DirtySerial);
                            break;
                    }
                }
            }

            if (_minimapMarkers == null)
            {
                return;
            }

            int markerCount = _minimapMarkers.Count;
            for (int i = 0; i < markerCount; i++)
            {
                Vector4 color = _minimapMarkers.GetColor(i);
                scene.TryAddMinimapMarker(
                    PresentationOverlayLayer.TopMost,
                    _minimapMarkers.GetScreenX(i),
                    _minimapMarkers.GetScreenY(i),
                    _minimapMarkers.GetSizePx(i),
                    in color,
                    _minimapMarkers.GetStableId(i),
                    dirtySerial: 0,
                    _minimapMarkers.GetFlags(i));
            }
        }

        private string? ResolveScreenHudText(in ScreenHudTextItem item)
        {
            bool allowResolvedCache = item.Text.HasValue || item.Id0 != 0 || item.Id1 != 0;
            if (allowResolvedCache &&
                item.StableId != 0 &&
                _screenHudResolvedTextCache.TryGetValue(item.StableId, out ScreenHudResolvedTextCacheEntry cached) &&
                cached.DirtySerial == item.DirtySerial)
            {
                return cached.Text;
            }

            if (TryFormatTextPacket(in item.Text, out string? packetText))
            {
                CacheResolvedScreenHudText(item, packetText, allowResolvedCache: true);
                return packetText;
            }

            if (item.Id0 != 0 && _worldHudStrings != null)
            {
                string? legacyText = _worldHudStrings.TryGet(item.Id0);
                CacheResolvedScreenHudText(item, legacyText, allowResolvedCache: true);
                return legacyText;
            }

            string? numericText = ResolveCachedNumericHudText(item.Id1, item.Value0, item.Value1);
            CacheResolvedScreenHudText(item, numericText, allowResolvedCache);
            return numericText;
        }

        private string? ResolveScreenOverlayText(in ScreenOverlayItem item)
        {
            if (TryFormatTextPacket(in item.Text, out string? packetText))
            {
                return packetText;
            }

            return _screenOverlay?.GetString(item.StringId);
        }

        private bool TryFormatTextPacket(in PresentationTextPacket packet, out string? text)
        {
            text = null;
            if (!packet.HasValue || _textCatalog == null || _localeSelection == null)
            {
                return false;
            }

            var cacheKey = new TextPacketCacheKey(_localeSelection.ActiveLocaleId, packet);
            if (_textPacketCache.TryGetValue(cacheKey, out string? cached))
            {
                text = cached;
                return true;
            }

            if (!PresentationTextFormatter.TryFormat(_textCatalog, _localeSelection.ActiveLocaleId, in packet, out string formatted))
            {
                return false;
            }

            if (_textPacketCache.Count >= MaxTextPacketCacheEntries)
            {
                _textPacketCache.Clear();
            }

            _textPacketCache[cacheKey] = formatted;
            text = formatted;
            return true;
        }

        private string? ResolveCachedNumericHudText(int modeId, float value0, float value1)
        {
            var cacheKey = new NumericTextCacheKey(modeId, BitConverter.SingleToInt32Bits(value0), BitConverter.SingleToInt32Bits(value1));
            if (_numericTextCache.TryGetValue(cacheKey, out string? cached))
            {
                return cached;
            }

            string? formatted = ResolveLegacyHudText(modeId, value0, value1);
            if (formatted == null)
            {
                return null;
            }

            if (_numericTextCache.Count >= MaxNumericTextCacheEntries)
            {
                _numericTextCache.Clear();
            }

            _numericTextCache[cacheKey] = formatted;
            return formatted;
        }

        private static string? ResolveLegacyHudText(int modeId, float value0, float value1)
        {
            WorldHudValueMode mode = (WorldHudValueMode)modeId;
            return mode switch
            {
                WorldHudValueMode.AttributeCurrentOverBase => $"{(int)value0}/{(int)value1}",
                WorldHudValueMode.AttributeCurrent => $"{(int)value0}",
                WorldHudValueMode.Constant => $"{value0}",
                _ => null
            };
        }

        private void CacheResolvedScreenHudText(in ScreenHudTextItem item, string? text, bool allowResolvedCache)
        {
            if (!allowResolvedCache || item.StableId == 0 || text == null)
            {
                return;
            }

            _screenHudResolvedTextCache[item.StableId] = new ScreenHudResolvedTextCacheEntry(item.DirtySerial, text);
        }

        private readonly record struct TextPacketCacheKey(
            int LocaleId,
            int TokenId,
            byte ArgCount,
            PresentationTextArg Arg0,
            PresentationTextArg Arg1,
            PresentationTextArg Arg2,
            PresentationTextArg Arg3)
        {
            public TextPacketCacheKey(int localeId, in PresentationTextPacket packet)
                : this(localeId, packet.TokenId, packet.ArgCount, packet.Arg0, packet.Arg1, packet.Arg2, packet.Arg3)
            {
            }
        }

        private readonly record struct NumericTextCacheKey(
            int ModeId,
            int Value0Bits,
            int Value1Bits);

        private readonly record struct ScreenHudResolvedTextCacheEntry(
            int DirtySerial,
            string Text);
    }
}
