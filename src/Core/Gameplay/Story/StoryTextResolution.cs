using System;
using System.Collections.Generic;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Gameplay.Story
{
    /// <summary>
    /// Single authority for resolving story TextTokens and speaker display names.
    /// Fail-closed: a missing resolver, unregistered token, or unregistered speaker is a
    /// configuration error — never degrade to showing raw token ids to players.
    /// </summary>
    public static class StoryTextResolution
    {
        public static string FormatToken(
            PresentationTextCatalog? catalog,
            PresentationDisplayResolver? display,
            string token,
            IReadOnlyList<Ludots.Core.Presentation.Hud.PresentationTextArg>? args = null)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Story text token is required.");
            }

            if (catalog == null)
            {
                // The catalog is the text SSOT; the display resolver is only a formatting fast path over it.
                throw new InvalidOperationException(
                    $"Story text token '{token}' cannot be resolved: PresentationTextCatalog is unavailable.");
            }

            if (display != null && args is not { Count: > 0 })
            {
                return display.FormatTokenOrThrow(token);
            }

            int tokenId = catalog.GetTokenId(token);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException(
                    $"Story text token '{token}' is not registered in PresentationTextCatalog.");
            }

            var packet = PresentationTextPacket.FromToken(tokenId);
            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    packet.SetArg(i, args[i]);
                }
            }

            if (!PresentationTextFormatter.TryFormat(catalog, catalog.DefaultLocaleId, in packet, out string text) ||
                string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException(
                    $"Story text token '{token}' has no locale template for default locale.");
            }

            return text;
        }

        public static IReadOnlyList<PresentationTextRun> FormatTokenRuns(
            PresentationTextCatalog? catalog,
            PresentationDisplayResolver? display,
            string token,
            IReadOnlyList<Ludots.Core.Presentation.Hud.PresentationTextArg>? args = null)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Story text token is required.");
            }

            if (catalog == null)
            {
                throw new InvalidOperationException(
                    $"Story text token '{token}' cannot be resolved: PresentationTextCatalog is unavailable.");
            }

            // display is retained for call-site symmetry with FormatToken; runs always go through catalog.
            _ = display;

            int tokenId = catalog.GetTokenId(token);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException(
                    $"Story text token '{token}' is not registered in PresentationTextCatalog.");
            }

            var packet = PresentationTextPacket.FromToken(tokenId);
            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    packet.SetArg(i, args[i]);
                }
            }

            if (!PresentationTextFormatter.TryFormatRuns(catalog, catalog.DefaultLocaleId, in packet, out IReadOnlyList<PresentationTextRun> runs) ||
                runs.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Story text token '{token}' has no locale template runs for default locale.");
            }

            return runs;
        }

        /// <summary>
        /// Empty speakerId yields an empty name (speaker-less subtitle tracks are legal);
        /// a non-empty id must reference a registered speaker whose token resolves.
        /// </summary>
        public static string ResolveSpeakerDisplayName(
            StoryDefinitionRegistry story,
            PresentationTextCatalog? catalog,
            PresentationDisplayResolver? display,
            string speakerId)
        {
            if (string.IsNullOrWhiteSpace(speakerId))
            {
                return string.Empty;
            }

            if (!story.TryGetSpeaker(speakerId, out StorySpeakerDefinition speaker))
            {
                throw new InvalidOperationException(
                    $"Speaker '{speakerId}' is not registered in Story/speakers.json.");
            }

            return FormatToken(catalog, display, speaker.DisplayNameToken);
        }
    }
}
