using Ludots.Core.Gameplay.GAS.Systems;

namespace Ludots.Tests.GAS
{
    public static class TestResponseChainOrderTypeIds
    {
        public const int ChainPass = 1;
        public const int ChainNegate = 2;
        public const int ChainActivateEffect = 3;

        public static ResponseChainOrderTypes Types => new ResponseChainOrderTypes
        {
            ChainPass = ChainPass,
            ChainNegate = ChainNegate,
            ChainActivateEffect = ChainActivateEffect
        };
    }
}
