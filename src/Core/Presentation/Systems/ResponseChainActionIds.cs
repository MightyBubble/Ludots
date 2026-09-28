using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    public readonly struct ResponseChainActionIds
    {
        public ResponseChainActionIds(string pass, string negate, string activate)
        {
            Pass = pass;
            Negate = negate;
            Activate = activate;
        }

        public string Pass { get; }
        public string Negate { get; }
        public string Activate { get; }

        public static ResponseChainActionIds Require(IReadOnlyDictionary<string, object> globals, string consumerName)
        {
            if (!globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var configObj) || configObj is not GameConfig config)
            {
                throw new InvalidOperationException(
                    $"{consumerName} requires GameConfig constants.responseChainActionIds (chainPass, chainNegate, chainActivateEffect).");
            }

            return new ResponseChainActionIds(
                RequireActionId(config, "chainPass", consumerName),
                RequireActionId(config, "chainNegate", consumerName),
                RequireActionId(config, "chainActivateEffect", consumerName));
        }

        private static string RequireActionId(GameConfig config, string key, string consumerName)
        {
            if (config.Constants?.ResponseChainActionIds == null ||
                !config.Constants.ResponseChainActionIds.TryGetValue(key, out string? actionId) ||
                string.IsNullOrWhiteSpace(actionId))
            {
                throw new InvalidOperationException(
                    $"{consumerName} requires GameConfig constants.responseChainActionIds.{key} to be a non-empty input action id.");
            }

            return actionId;
        }
    }
}
