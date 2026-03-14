using System;
using System.Collections.Generic;
using Ludots.Core.Presentation.Config;

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
        private readonly Dictionary<TextPacketCacheKey, string> _textPacketCache = new();
        private readonly Dictionary<NumericTextCacheKey, string> _numericTextCache = new();
        private readonly Dictionary<int, ScreenHudResolvedTextCacheEntry> _screenHudResolvedTextCache = new();

        public PresentationOverlaySceneBuilder(
            ScreenHudBatchBuffer screenHud,
            WorldHudStringTable? worldHudStrings,
            PresentationTextCatalog? textCatalog,
            PresentationTextLocaleSelection? localeSelection,
            ScreenOverlayBuffer? screenOverlay)
        {
            _screenHud = screenHud ?? throw new ArgumentNullException(nameof(screenHud));
            _worldHudStrings = worldHudStrings;
            _textCatalog = textCatalog;
            _localeSelection = localeSelection;
            _screenOverlay = screenOverlay;
        }

        public void Build(PresentationOverlayScene scene)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            scene.BeginBuild();
            AppendScreenHud(scene);
            AppendScreenOverlay(scene);
            scene.EndBuild();
        }

        private void AppendScreenHud(PresentationOverlayScene scene)
        {
            ReadOnlySpan<ScreenHudBarItem> bars = _screenHud.GetBarSpan();
            for (int i = 0; i < bars.Length; i++)
            {
                ref readonly ScreenHudBarItem item = ref bars[i];
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
            if (_screenOverlay == null)
            {
                return;
            }

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
                                item.Color);
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
                            item.Color);
                        break;
                }
            }
        }

        private string? ResolveScreenHudText(in ScreenHudTextItem item)
        {
            if (item.StableId != 0 &&
                _screenHudResolvedTextCache.TryGetValue(item.StableId, out ScreenHudResolvedTextCacheEntry cached) &&
                cached.DirtySerial == item.DirtySerial)
            {
                return cached.Text;
            }

            if (TryFormatTextPacket(in item.Text, out string? packetText))
            {
                CacheResolvedScreenHudText(in item, packetText);
                return packetText;
            }

            if (item.Id0 != 0 && _worldHudStrings != null)
            {
                string? legacyText = _worldHudStrings.TryGet(item.Id0);
                CacheResolvedScreenHudText(in item, legacyText);
                return legacyText;
            }

            string? numericText = ResolveCachedNumericHudText(item.Id1, item.Value0, item.Value1);
            CacheResolvedScreenHudText(in item, numericText);
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

        private void CacheResolvedScreenHudText(in ScreenHudTextItem item, string? text)
        {
            if (item.StableId == 0 || text == null)
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
