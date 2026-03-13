using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib
{
    internal sealed class RaylibHudTextRenderLane : IDisposable
    {
        private const int DefaultFontSize = 16;
        private const int MaxPacketSprites = 4096;
        private const int MaxNumericSprites = 2048;
        private const int MaxLegacySprites = 2048;

        private readonly Dictionary<PacketSpriteKey, CachedSprite> _packetSprites = new();
        private readonly Dictionary<NumericSpriteKey, CachedSprite> _numericSprites = new();
        private readonly Dictionary<LegacySpriteKey, CachedSprite> _legacySprites = new();

        private int _activeLocaleId = -1;

        public void BeginFrame(int localeId)
        {
            if (localeId == _activeLocaleId)
            {
                return;
            }

            ClearAll();
            _activeLocaleId = localeId;
        }

        public bool TryDrawHudText(GameEngine engine, WorldHudStringTable? strings, in ScreenHudItem item)
        {
            int fontSize = item.FontSize <= 0 ? DefaultFontSize : item.FontSize;
            if (TryResolvePacketTexture(engine, in item.Text, fontSize, out Texture2D texture) ||
                TryResolveLegacyStringTexture(strings?.TryGet(item.Id0), fontSize, out texture) ||
                TryResolveNumericTexture((WorldHudValueMode)item.Id1, item.Value0, item.Value1, fontSize, out texture))
            {
                Rl.DrawTexture(texture, (int)item.ScreenX, (int)item.ScreenY, RaylibColorUtil.ToRaylibColor(in item.Color0));
                return true;
            }

            return false;
        }

        public bool TryDrawOverlayPacketText(GameEngine engine, in ScreenOverlayItem item)
        {
            int fontSize = item.FontSize <= 0 ? DefaultFontSize : item.FontSize;
            if (!TryResolvePacketTexture(engine, in item.Text, fontSize, out Texture2D texture))
            {
                return false;
            }

            Rl.DrawTexture(texture, item.X, item.Y, RaylibColorUtil.ToRaylibColor(in item.Color));
            return true;
        }

        public void Dispose()
        {
            ClearAll();
        }

        private bool TryResolvePacketTexture(GameEngine engine, in PresentationTextPacket packet, int fontSize, out Texture2D texture)
        {
            texture = default;
            if (!packet.HasValue)
            {
                return false;
            }

            PresentationTextCatalog? catalog = engine.GetService(CoreServiceKeys.PresentationTextCatalog);
            PresentationTextLocaleSelection? localeSelection = engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection);
            if (catalog == null || localeSelection == null)
            {
                return false;
            }

            var key = new PacketSpriteKey(localeSelection.ActiveLocaleId, fontSize, in packet);
            if (_packetSprites.TryGetValue(key, out CachedSprite cached))
            {
                texture = cached.Texture;
                return true;
            }

            if (!PresentationTextFormatter.TryFormat(catalog, localeSelection.ActiveLocaleId, in packet, out string text) ||
                string.IsNullOrEmpty(text))
            {
                return false;
            }

            EnsureRoom(_packetSprites, MaxPacketSprites);
            texture = CreateTexture(text, fontSize);
            _packetSprites[key] = new CachedSprite(texture);
            return true;
        }

        private bool TryResolveLegacyStringTexture(string? text, int fontSize, out Texture2D texture)
        {
            texture = default;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var key = new LegacySpriteKey(fontSize, text);
            if (_legacySprites.TryGetValue(key, out CachedSprite cached))
            {
                texture = cached.Texture;
                return true;
            }

            EnsureRoom(_legacySprites, MaxLegacySprites);
            texture = CreateTexture(text, fontSize);
            _legacySprites[key] = new CachedSprite(texture);
            return true;
        }

        private bool TryResolveNumericTexture(WorldHudValueMode mode, float value0, float value1, int fontSize, out Texture2D texture)
        {
            texture = default;
            if (!TryFormatNumeric(mode, value0, value1, out string text))
            {
                return false;
            }

            var key = new NumericSpriteKey(fontSize, (int)mode, BitConverter.SingleToInt32Bits(value0), BitConverter.SingleToInt32Bits(value1));
            if (_numericSprites.TryGetValue(key, out CachedSprite cached))
            {
                texture = cached.Texture;
                return true;
            }

            EnsureRoom(_numericSprites, MaxNumericSprites);
            texture = CreateTexture(text, fontSize);
            _numericSprites[key] = new CachedSprite(texture);
            return true;
        }

        private static bool TryFormatNumeric(WorldHudValueMode mode, float value0, float value1, out string text)
        {
            switch (mode)
            {
                case WorldHudValueMode.AttributeCurrentOverBase:
                    text = $"{(int)value0}/{(int)value1}";
                    return true;

                case WorldHudValueMode.AttributeCurrent:
                    text = $"{(int)value0}";
                    return true;

                case WorldHudValueMode.Constant:
                    text = $"{value0}";
                    return true;

                default:
                    text = string.Empty;
                    return false;
            }
        }

        private static unsafe Texture2D CreateTexture(string text, int fontSize)
        {
            int width = Math.Max(1, Rl.MeasureText(text, fontSize) + 2);
            int height = Math.Max(1, fontSize + 2);
            Image image = Rl.GenImageColor(width, height, Color.BLANK);
            try
            {
                Rl.ImageDrawText(&image, text, 0, 0, fontSize, Color.WHITE);
                return Rl.LoadTextureFromImage(image);
            }
            finally
            {
                Rl.UnloadImage(image);
            }
        }

        private void ClearAll()
        {
            UnloadSprites(_packetSprites);
            UnloadSprites(_numericSprites);
            UnloadSprites(_legacySprites);
        }

        private static void EnsureRoom<TKey>(Dictionary<TKey, CachedSprite> cache, int maxEntries) where TKey : notnull
        {
            if (cache.Count < maxEntries)
            {
                return;
            }

            UnloadSprites(cache);
        }

        private static void UnloadSprites<TKey>(Dictionary<TKey, CachedSprite> cache) where TKey : notnull
        {
            foreach (CachedSprite sprite in cache.Values)
            {
                if (sprite.Texture.id != 0)
                {
                    Rl.UnloadTexture(sprite.Texture);
                }
            }

            cache.Clear();
        }

        private readonly struct CachedSprite
        {
            public CachedSprite(Texture2D texture)
            {
                Texture = texture;
            }

            public Texture2D Texture { get; }
        }

        private readonly struct LegacySpriteKey : IEquatable<LegacySpriteKey>
        {
            public LegacySpriteKey(int fontSize, string text)
            {
                FontSize = fontSize;
                Text = text ?? string.Empty;
            }

            public int FontSize { get; }
            public string Text { get; }

            public bool Equals(LegacySpriteKey other)
            {
                return FontSize == other.FontSize &&
                       string.Equals(Text, other.Text, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj) => obj is LegacySpriteKey other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(FontSize);
                hash.Add(Text, StringComparer.Ordinal);
                return hash.ToHashCode();
            }
        }

        private readonly struct NumericSpriteKey : IEquatable<NumericSpriteKey>
        {
            public NumericSpriteKey(int fontSize, int mode, int value0Bits, int value1Bits)
            {
                FontSize = fontSize;
                Mode = mode;
                Value0Bits = value0Bits;
                Value1Bits = value1Bits;
            }

            public int FontSize { get; }
            public int Mode { get; }
            public int Value0Bits { get; }
            public int Value1Bits { get; }

            public bool Equals(NumericSpriteKey other)
            {
                return FontSize == other.FontSize &&
                       Mode == other.Mode &&
                       Value0Bits == other.Value0Bits &&
                       Value1Bits == other.Value1Bits;
            }

            public override bool Equals(object? obj) => obj is NumericSpriteKey other && Equals(other);

            public override int GetHashCode()
            {
                return HashCode.Combine(FontSize, Mode, Value0Bits, Value1Bits);
            }
        }

        private readonly struct PacketSpriteKey : IEquatable<PacketSpriteKey>
        {
            private readonly int _localeId;
            private readonly int _fontSize;
            private readonly int _tokenId;
            private readonly byte _argCount;
            private readonly PresentationTextArg _arg0;
            private readonly PresentationTextArg _arg1;
            private readonly PresentationTextArg _arg2;
            private readonly PresentationTextArg _arg3;

            public PacketSpriteKey(int localeId, int fontSize, in PresentationTextPacket packet)
            {
                _localeId = localeId;
                _fontSize = fontSize;
                _tokenId = packet.TokenId;
                _argCount = packet.ArgCount;
                _arg0 = packet.Arg0;
                _arg1 = packet.Arg1;
                _arg2 = packet.Arg2;
                _arg3 = packet.Arg3;
            }

            public bool Equals(PacketSpriteKey other)
            {
                return _localeId == other._localeId &&
                       _fontSize == other._fontSize &&
                       _tokenId == other._tokenId &&
                       _argCount == other._argCount &&
                       ArgsEqual(_arg0, other._arg0) &&
                       ArgsEqual(_arg1, other._arg1) &&
                       ArgsEqual(_arg2, other._arg2) &&
                       ArgsEqual(_arg3, other._arg3);
            }

            public override bool Equals(object? obj) => obj is PacketSpriteKey other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(_localeId);
                hash.Add(_fontSize);
                hash.Add(_tokenId);
                hash.Add(_argCount);
                AddArgHash(ref hash, _arg0);
                AddArgHash(ref hash, _arg1);
                AddArgHash(ref hash, _arg2);
                AddArgHash(ref hash, _arg3);
                return hash.ToHashCode();
            }

            private static bool ArgsEqual(in PresentationTextArg left, in PresentationTextArg right)
            {
                return left.Type == right.Type &&
                       left.Format == right.Format &&
                       left.Raw32 == right.Raw32;
            }

            private static void AddArgHash(ref HashCode hash, in PresentationTextArg arg)
            {
                hash.Add((byte)arg.Type);
                hash.Add((byte)arg.Format);
                hash.Add(arg.Raw32);
            }
        }
    }
}
