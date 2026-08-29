using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ludots.Core.Presentation.Hud
{
    public static class PresentationTextFormatter
    {
        public static bool TryFormat(
            PresentationTextCatalog catalog,
            int localeId,
            in PresentationTextPacket packet,
            out string text)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            if (!packet.HasValue || !catalog.TryGetTemplate(localeId, packet.TokenId, out var template))
            {
                text = string.Empty;
                return false;
            }

            text = Format(template, in packet, catalog.StringPool);
            return true;
        }

        public static bool TryFormatRuns(
            PresentationTextCatalog catalog,
            int localeId,
            in PresentationTextPacket packet,
            out IReadOnlyList<PresentationTextRun> runs)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            if (!packet.HasValue || !catalog.TryGetTemplate(localeId, packet.TokenId, out var template))
            {
                runs = Array.Empty<PresentationTextRun>();
                return false;
            }

            runs = FormatRuns(template, in packet, catalog.StringPool);
            return true;
        }

        public static string Format(
            PresentationTextTemplate template,
            in PresentationTextPacket packet,
            PresentationTextStringPool? stringPool = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            var builder = new StringBuilder(Math.Max(template.Source.Length, packet.ArgCount * 8));
            AppendFormatted(builder, template, in packet, stringPool);
            return builder.ToString();
        }

        public static IReadOnlyList<PresentationTextRun> FormatRuns(
            PresentationTextTemplate template,
            in PresentationTextPacket packet,
            PresentationTextStringPool? stringPool = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            if (!template.HasStyledParts)
            {
                string plain = Format(template, in packet, stringPool);
                if (string.IsNullOrEmpty(plain))
                {
                    return Array.Empty<PresentationTextRun>();
                }

                return new[] { new PresentationTextRun(plain, PresentationTextStyleOverride.None) };
            }

            var runs = new List<PresentationTextRun>(template.GetParts().Length);
            var scratch = new StringBuilder(32);
            ReadOnlySpan<PresentationTextTemplatePart> parts = template.GetParts();
            for (int i = 0; i < parts.Length; i++)
            {
                PresentationTextTemplatePart part = parts[i];
                scratch.Clear();
                if (part.Kind == PresentationTextTemplatePartKind.Literal ||
                    part.Kind == PresentationTextTemplatePartKind.StyledLiteral)
                {
                    scratch.Append(part.Literal);
                }
                else if ((uint)part.ArgIndex < packet.ArgCount)
                {
                    AppendArg(scratch, packet.GetArg(part.ArgIndex), stringPool);
                }

                if (scratch.Length == 0)
                {
                    continue;
                }

                runs.Add(new PresentationTextRun(scratch.ToString(), part.Style));
            }

            return runs;
        }

        public static void AppendFormatted(
            StringBuilder builder,
            PresentationTextTemplate template,
            in PresentationTextPacket packet,
            PresentationTextStringPool? stringPool = null)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (template == null) throw new ArgumentNullException(nameof(template));

            ReadOnlySpan<PresentationTextTemplatePart> parts = template.GetParts();
            for (int i = 0; i < parts.Length; i++)
            {
                PresentationTextTemplatePart part = parts[i];
                if (part.Kind == PresentationTextTemplatePartKind.Literal ||
                    part.Kind == PresentationTextTemplatePartKind.StyledLiteral)
                {
                    builder.Append(part.Literal);
                    continue;
                }

                if (part.Kind != PresentationTextTemplatePartKind.Argument)
                {
                    continue;
                }

                if ((uint)part.ArgIndex >= packet.ArgCount)
                {
                    continue;
                }

                PresentationTextArg arg = packet.GetArg(part.ArgIndex);
                AppendArg(builder, in arg, stringPool);
            }
        }

        private static void AppendArg(
            StringBuilder builder,
            in PresentationTextArg arg,
            PresentationTextStringPool? stringPool)
        {
            switch (arg.Type)
            {
                case PresentationTextArgType.Int32:
                    builder.Append(arg.AsInt32().ToString(CultureInfo.InvariantCulture));
                    break;

                case PresentationTextArgType.Float32:
                    AppendFloat(builder, arg.AsFloat32(), arg.Format);
                    break;

                case PresentationTextArgType.String:
                    if (stringPool == null)
                    {
                        throw new InvalidOperationException(
                            "Presentation text string arg requires the owning catalog string pool.");
                    }

                    builder.Append(stringPool.Get(in arg));
                    break;
            }
        }

        private static void AppendFloat(StringBuilder builder, float value, PresentationTextArgFormat format)
        {
            string formatted = format switch
            {
                PresentationTextArgFormat.Integer => ((int)value).ToString(CultureInfo.InvariantCulture),
                PresentationTextArgFormat.Fixed0 => value.ToString("0", CultureInfo.InvariantCulture),
                PresentationTextArgFormat.Fixed1 => value.ToString("0.0", CultureInfo.InvariantCulture),
                PresentationTextArgFormat.Fixed2 => value.ToString("0.00", CultureInfo.InvariantCulture),
                _ => value.ToString("0.###", CultureInfo.InvariantCulture),
            };

            builder.Append(formatted);
        }
    }
}
