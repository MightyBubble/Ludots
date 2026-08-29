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

            if (display != null && args is not { Count: > 0 })
            {
                // Display resolver is the zero-arg fast path; parameterized tokens format via the catalog below.
                return display.FormatTokenOrThrow(token);
            }

            if (catalog == null)
            {
                throw new InvalidOperationException(
                    $"Story text token '{token}' cannot be resolved: neither PresentationDisplayResolver nor PresentationTextCatalog is available.");
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
