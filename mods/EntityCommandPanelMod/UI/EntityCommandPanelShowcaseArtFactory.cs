using System;
using System.Collections.Generic;

namespace EntityCommandPanelMod.UI
{
    internal sealed class EntityCommandPanelShowcaseArtFactory
    {
        private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

        public string BuildSlotArt(
            string themeId,
            string glyph,
            string hotkey,
            string accentColorHex,
            short cooldownPermille,
            bool blocked,
            bool active,
            bool empty)
        {
            string normalizedTheme = EntityCommandPanelShowcaseTheme.Normalize(themeId, EntityCommandPanelShowcaseTheme.LolId);
            string normalizedGlyph = string.IsNullOrWhiteSpace(glyph) ? "-" : glyph.Trim();
            string normalizedHotkey = string.IsNullOrWhiteSpace(hotkey) ? string.Empty : hotkey.Trim();
            string normalizedAccent = NormalizeHex(accentColorHex, empty ? "#5E6B7C" : "#59B7FF");
            string key = string.Concat(
                normalizedTheme, "|",
                normalizedGlyph, "|",
                normalizedHotkey, "|",
                normalizedAccent, "|",
                cooldownPermille.ToString(System.Globalization.CultureInfo.InvariantCulture), "|",
                blocked ? "1" : "0", "|",
                active ? "1" : "0", "|",
                empty ? "1" : "0");

            if (_cache.TryGetValue(key, out string? cached))
            {
                return cached;
            }

            string svg = normalizedTheme switch
            {
                var theme when string.Equals(theme, EntityCommandPanelShowcaseTheme.Dota2Id, StringComparison.Ordinal) => BuildDotaSlotSvg(normalizedGlyph, normalizedHotkey, normalizedAccent, cooldownPermille, blocked, active, empty),
                var theme when string.Equals(theme, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.Ordinal) => BuildSc2SlotSvg(normalizedGlyph, normalizedHotkey, normalizedAccent, cooldownPermille, blocked, active, empty),
                _ => BuildLolSlotSvg(normalizedGlyph, normalizedHotkey, normalizedAccent, cooldownPermille, blocked, active, empty)
            };

            string uri = "data:image/svg+xml;utf8," + Uri.EscapeDataString(svg);
            _cache[key] = uri;
            return uri;
        }

        public string BuildPortraitArt(string themeId, string title, string subtitle, string accentColorHex)
        {
            string normalizedTheme = EntityCommandPanelShowcaseTheme.Normalize(themeId, EntityCommandPanelShowcaseTheme.LolId);
            string normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Unknown" : title.Trim();
            string normalizedSubtitle = string.IsNullOrWhiteSpace(subtitle) ? "Showcase" : subtitle.Trim();
            string normalizedAccent = NormalizeHex(accentColorHex, "#59B7FF");
            string key = string.Concat("portrait|", normalizedTheme, "|", normalizedTitle, "|", normalizedSubtitle, "|", normalizedAccent);
            if (_cache.TryGetValue(key, out string? cached))
            {
                return cached;
            }

            string svg = normalizedTheme switch
            {
                var theme when string.Equals(theme, EntityCommandPanelShowcaseTheme.Dota2Id, StringComparison.Ordinal) => BuildDotaPortraitSvg(normalizedTitle, normalizedSubtitle, normalizedAccent),
                var theme when string.Equals(theme, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.Ordinal) => BuildSc2PortraitSvg(normalizedTitle, normalizedSubtitle, normalizedAccent),
                _ => BuildLolPortraitSvg(normalizedTitle, normalizedSubtitle, normalizedAccent)
            };

            string uri = "data:image/svg+xml;utf8," + Uri.EscapeDataString(svg);
            _cache[key] = uri;
            return uri;
        }

        private static string BuildLolSlotSvg(string glyph, string hotkey, string accent, short cooldownPermille, bool blocked, bool active, bool empty)
        {
            string fill = empty ? "#1A222D" : "#0B1118";
            string inner = empty ? "#202B38" : "#17222F";
            string border = active ? "#D6B56A" : "#3B4D62";
            string glow = active ? "0.95" : "0.42";
            string cooldownOverlay = cooldownPermille > 0
                ? $"<rect x='8' y='8' width='112' height='112' rx='14' fill='#020406' opacity='0.62'/>" +
                  $"<text x='64' y='73' text-anchor='middle' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='26' font-weight='800' fill='#F2F7FF'>{cooldownPermille / 10f:0}%</text>"
                : string.Empty;
            string blockedStroke = blocked ? "<path d='M20 104 L108 16' stroke='#FB5F69' stroke-width='8' stroke-linecap='round' opacity='0.9'/>" : string.Empty;
            string hotkeyBadge = string.IsNullOrWhiteSpace(hotkey)
                ? string.Empty
                : $"<rect x='40' y='122' width='48' height='20' rx='10' fill='#0A0F15' stroke='#7B6840' stroke-width='1.4'/>" +
                  $"<text x='64' y='136' text-anchor='middle' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='11' font-weight='700' fill='#E7D18B'>{EscapeXml(hotkey)}</text>";

            return
                "<svg xmlns='http://www.w3.org/2000/svg' width='128' height='148' viewBox='0 0 128 148'>" +
                "<defs>" +
                $"<linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0%' stop-color='{accent}' stop-opacity='0.96'/><stop offset='100%' stop-color='#1B2735' stop-opacity='0.88'/></linearGradient>" +
                "</defs>" +
                $"<rect x='6' y='6' width='116' height='116' rx='16' fill='{fill}' stroke='#8E7442' stroke-width='3'/>" +
                $"<rect x='10' y='10' width='108' height='108' rx='14' fill='{inner}' stroke='{border}' stroke-width='2'/>" +
                $"<rect x='16' y='16' width='96' height='96' rx='12' fill='url(#g)' opacity='0.96'/>" +
                $"<rect x='16' y='16' width='96' height='96' rx='12' fill='none' stroke='{accent}' stroke-width='2.4' opacity='{glow}'/>" +
                $"<text x='64' y='76' text-anchor='middle' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='42' font-weight='900' fill='#F4F8FD'>{EscapeXml(glyph)}</text>" +
                cooldownOverlay +
                blockedStroke +
                hotkeyBadge +
                "</svg>";
        }

        private static string BuildDotaSlotSvg(string glyph, string hotkey, string accent, short cooldownPermille, bool blocked, bool active, bool empty)
        {
            string fill = empty ? "#201915" : "#16100C";
            string inner = empty ? "#2B231D" : "#2A1A10";
            string border = active ? "#D6B07A" : "#5C4632";
            string cooldownOverlay = cooldownPermille > 0
                ? $"<rect x='10' y='10' width='108' height='108' rx='10' fill='#020100' opacity='0.68'/>" +
                  $"<text x='64' y='74' text-anchor='middle' font-family='Georgia, Times New Roman, serif' font-size='28' font-weight='700' fill='#F5E6CC'>{cooldownPermille / 10f:0}%</text>"
                : string.Empty;
            string blockedStroke = blocked ? "<path d='M18 106 L110 14' stroke='#9E3C31' stroke-width='9' stroke-linecap='round' opacity='0.88'/>" : string.Empty;
            string hotkeyBadge = string.IsNullOrWhiteSpace(hotkey)
                ? string.Empty
                : $"<rect x='8' y='122' width='28' height='18' rx='4' fill='#271D17' stroke='#84654C' stroke-width='1.2'/>" +
                  $"<text x='22' y='135' text-anchor='middle' font-family='Georgia, Times New Roman, serif' font-size='11' font-weight='700' fill='#E5D7B8'>{EscapeXml(hotkey)}</text>";

            return
                "<svg xmlns='http://www.w3.org/2000/svg' width='128' height='148' viewBox='0 0 128 148'>" +
                "<defs>" +
                $"<linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0%' stop-color='{accent}' stop-opacity='0.9'/><stop offset='65%' stop-color='#413022' stop-opacity='0.96'/><stop offset='100%' stop-color='#100C09' stop-opacity='1'/></linearGradient>" +
                "</defs>" +
                $"<rect x='4' y='4' width='120' height='120' rx='12' fill='{fill}' stroke='#8A6A46' stroke-width='3'/>" +
                $"<rect x='10' y='10' width='108' height='108' rx='10' fill='{inner}' stroke='{border}' stroke-width='2'/>" +
                $"<rect x='14' y='14' width='100' height='100' rx='8' fill='url(#g)'/>" +
                $"<rect x='14' y='14' width='100' height='100' rx='8' fill='none' stroke='#D2A264' stroke-width='1.8' opacity='0.72'/>" +
                $"<text x='64' y='76' text-anchor='middle' font-family='Georgia, Times New Roman, serif' font-size='42' font-weight='700' fill='#F7E8D1'>{EscapeXml(glyph)}</text>" +
                cooldownOverlay +
                blockedStroke +
                hotkeyBadge +
                "</svg>";
        }

        private static string BuildSc2SlotSvg(string glyph, string hotkey, string accent, short cooldownPermille, bool blocked, bool active, bool empty)
        {
            string fill = empty ? "#101D2B" : "#09131F";
            string inner = empty ? "#183043" : "#102437";
            string border = active ? "#7FDBFF" : "#2D4C65";
            string cooldownOverlay = cooldownPermille > 0
                ? $"<rect x='8' y='8' width='112' height='112' rx='6' fill='#02060B' opacity='0.74'/>" +
                  $"<text x='64' y='74' text-anchor='middle' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='26' font-weight='800' fill='#CFEFFF'>{cooldownPermille / 10f:0}%</text>"
                : string.Empty;
            string blockedStroke = blocked ? "<path d='M18 108 L110 16' stroke='#E45B4F' stroke-width='8' stroke-linecap='round' opacity='0.92'/>" : string.Empty;
            string hotkeyBadge = string.IsNullOrWhiteSpace(hotkey)
                ? string.Empty
                : $"<rect x='90' y='122' width='28' height='18' rx='3' fill='#071019' stroke='#4A84A8' stroke-width='1.2'/>" +
                  $"<text x='104' y='135' text-anchor='middle' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='11' font-weight='700' fill='#D6F3FF'>{EscapeXml(hotkey)}</text>";

            return
                "<svg xmlns='http://www.w3.org/2000/svg' width='128' height='148' viewBox='0 0 128 148'>" +
                "<defs>" +
                $"<linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0%' stop-color='{accent}' stop-opacity='0.9'/><stop offset='60%' stop-color='#143A58' stop-opacity='0.95'/><stop offset='100%' stop-color='#08111B' stop-opacity='1'/></linearGradient>" +
                "</defs>" +
                $"<rect x='4' y='4' width='120' height='120' rx='8' fill='{fill}' stroke='#204059' stroke-width='3'/>" +
                $"<rect x='9' y='9' width='110' height='110' rx='6' fill='{inner}' stroke='{border}' stroke-width='1.8'/>" +
                $"<rect x='14' y='14' width='100' height='100' rx='4' fill='url(#g)' opacity='0.96'/>" +
                $"<path d='M14 36 L114 36' stroke='#8BE9FF' stroke-width='1.2' opacity='0.45'/>" +
                $"<path d='M14 92 L114 92' stroke='#8BE9FF' stroke-width='1.2' opacity='0.18'/>" +
                $"<text x='64' y='76' text-anchor='middle' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='42' font-weight='900' fill='#ECFBFF'>{EscapeXml(glyph)}</text>" +
                cooldownOverlay +
                blockedStroke +
                hotkeyBadge +
                "</svg>";
        }

        private static string BuildLolPortraitSvg(string title, string subtitle, string accent)
        {
            return
                "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='164' viewBox='0 0 200 164'>" +
                "<defs>" +
                $"<linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0%' stop-color='{accent}' stop-opacity='0.95'/><stop offset='100%' stop-color='#0B1118' stop-opacity='1'/></linearGradient>" +
                "</defs>" +
                "<rect width='200' height='164' rx='18' fill='#081018' stroke='#8E7442' stroke-width='3'/>" +
                "<rect x='10' y='10' width='180' height='98' rx='14' fill='url(#g)'/>" +
                "<circle cx='100' cy='56' r='28' fill='#08131D' stroke='#E0C679' stroke-width='3'/>" +
                $"<text x='100' y='66' text-anchor='middle' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='28' font-weight='900' fill='#F4F8FD'>{EscapeXml(title[..1].ToUpperInvariant())}</text>" +
                $"<text x='18' y='128' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='16' font-weight='700' fill='#F1DC97'>{EscapeXml(title)}</text>" +
                $"<text x='18' y='148' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='11' font-weight='600' fill='#C5D3DE'>{EscapeXml(subtitle)}</text>" +
                "</svg>";
        }

        private static string BuildDotaPortraitSvg(string title, string subtitle, string accent)
        {
            return
                "<svg xmlns='http://www.w3.org/2000/svg' width='220' height='170' viewBox='0 0 220 170'>" +
                "<defs>" +
                $"<linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0%' stop-color='{accent}' stop-opacity='0.92'/><stop offset='100%' stop-color='#17110C' stop-opacity='1'/></linearGradient>" +
                "</defs>" +
                "<rect width='220' height='170' rx='16' fill='#120E0B' stroke='#8A6A46' stroke-width='3'/>" +
                "<rect x='12' y='12' width='196' height='108' rx='12' fill='url(#g)'/>" +
                "<path d='M20 98 L110 26 L200 98' fill='none' stroke='#D6B07A' stroke-width='4' opacity='0.48'/>" +
                $"<text x='110' y='72' text-anchor='middle' font-family='Georgia, Times New Roman, serif' font-size='32' font-weight='700' fill='#F5E6CC'>{EscapeXml(title[..1].ToUpperInvariant())}</text>" +
                $"<text x='18' y='140' font-family='Georgia, Times New Roman, serif' font-size='18' font-weight='700' fill='#F4DEB7'>{EscapeXml(title)}</text>" +
                $"<text x='18' y='158' font-family='Georgia, Times New Roman, serif' font-size='11' font-weight='700' fill='#C8B79F'>{EscapeXml(subtitle)}</text>" +
                "</svg>";
        }

        private static string BuildSc2PortraitSvg(string title, string subtitle, string accent)
        {
            return
                "<svg xmlns='http://www.w3.org/2000/svg' width='196' height='196' viewBox='0 0 196 196'>" +
                "<defs>" +
                $"<linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0%' stop-color='{accent}' stop-opacity='0.92'/><stop offset='100%' stop-color='#07111A' stop-opacity='1'/></linearGradient>" +
                "</defs>" +
                "<rect width='196' height='196' rx='12' fill='#06121D' stroke='#2E5A79' stroke-width='3'/>" +
                "<rect x='10' y='10' width='176' height='116' rx='8' fill='url(#g)'/>" +
                "<path d='M10 44 L186 44' stroke='#8BE9FF' stroke-width='1.2' opacity='0.32'/>" +
                "<path d='M10 94 L186 94' stroke='#8BE9FF' stroke-width='1.2' opacity='0.2'/>" +
                "<circle cx='98' cy='68' r='28' fill='#08141E' stroke='#7FDBFF' stroke-width='3'/>" +
                $"<text x='98' y='79' text-anchor='middle' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='28' font-weight='900' fill='#ECFBFF'>{EscapeXml(title[..1].ToUpperInvariant())}</text>" +
                $"<text x='16' y='150' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='17' font-weight='700' fill='#D7F6FF'>{EscapeXml(title)}</text>" +
                $"<text x='16' y='170' font-family='Segoe UI, Microsoft YaHei, sans-serif' font-size='11' font-weight='700' fill='#8FB9D1'>{EscapeXml(subtitle)}</text>" +
                "</svg>";
        }

        private static string NormalizeHex(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith('#'))
            {
                trimmed = trimmed[1..];
            }

            if (trimmed.Length == 3)
            {
                trimmed = string.Concat(
                    trimmed[0], trimmed[0],
                    trimmed[1], trimmed[1],
                    trimmed[2], trimmed[2]);
            }

            if (trimmed.Length < 6)
            {
                return fallback;
            }

            string rgb = trimmed[..6];
            for (int i = 0; i < rgb.Length; i++)
            {
                char ch = rgb[i];
                bool isHex = (ch >= '0' && ch <= '9') ||
                             (ch >= 'A' && ch <= 'F') ||
                             (ch >= 'a' && ch <= 'f');
                if (!isHex)
                {
                    return fallback;
                }
            }

            return "#" + rgb.ToUpperInvariant();
        }

        private static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&apos;", StringComparison.Ordinal);
        }
    }
}
