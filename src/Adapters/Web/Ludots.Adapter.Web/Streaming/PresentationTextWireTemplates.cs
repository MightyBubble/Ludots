using System;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Adapter.Web.Streaming
{
    /// <summary>
    /// 句子模板和名字孔必须一起下发。表装不下就失败，不能把名字丢掉。
    /// </summary>
    internal static class PresentationTextWireTemplates
    {
        public const int StackItemLimit = 128;

        public static int RequiredCapacity(int itemCount)
        {
            if (itemCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemCount));
            }

            return checked(itemCount * (1 + PresentationTextPacket.MaxArgs));
        }

        public static int CollectUniqueTokenIds(
            ReadOnlySpan<ScreenHudItem> items,
            WorldHudStringTable? strings,
            Span<int> tokenIds)
        {
            int tokenCount = 0;
            for (int i = 0; i < items.Length; i++)
            {
                tokenCount = AddPacket(in items[i].Text, strings, tokenIds, tokenCount);
            }

            return tokenCount;
        }

        public static int CollectUniqueTokenIds(
            ReadOnlySpan<ScreenOverlayItem> items,
            WorldHudStringTable? strings,
            Span<int> tokenIds)
        {
            int tokenCount = 0;
            for (int i = 0; i < items.Length; i++)
            {
                tokenCount = AddPacket(in items[i].Text, strings, tokenIds, tokenCount);
            }

            return tokenCount;
        }

        private static int AddPacket(
            in PresentationTextPacket packet,
            WorldHudStringTable? strings,
            Span<int> tokenIds,
            int tokenCount)
        {
            tokenCount = AddToken(packet.TokenId, strings, tokenIds, tokenCount);
            int argCount = packet.ArgCount;
            for (int i = 0; i < argCount; i++)
            {
                PresentationTextArg arg = packet.GetArg(i);
                if (arg.Type == PresentationTextArgType.TextToken)
                {
                    tokenCount = AddToken(arg.Raw32, strings, tokenIds, tokenCount);
                }
            }

            return tokenCount;
        }

        private static int AddToken(
            int tokenId,
            WorldHudStringTable? strings,
            Span<int> tokenIds,
            int tokenCount)
        {
            if (tokenId <= 0 || strings?.TryGet(tokenId) == null)
            {
                return tokenCount;
            }

            for (int i = 0; i < tokenCount; i++)
            {
                if (tokenIds[i] == tokenId)
                {
                    return tokenCount;
                }
            }

            if ((uint)tokenCount >= (uint)tokenIds.Length)
            {
                throw new InvalidOperationException(
                    $"Presentation text template table overflowed while collecting token {tokenId}.");
            }

            tokenIds[tokenCount] = tokenId;
            return tokenCount + 1;
        }
    }
}
