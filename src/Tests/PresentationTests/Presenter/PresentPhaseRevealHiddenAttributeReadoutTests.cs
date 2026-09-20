using System.Collections.Generic;
using NUnit.Framework;
using Ludots.Core.Knowledge;
using Ludots.Core.Scripting;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// #1491 收割语义的合同：reveal-hidden（全图/benchmark showcase）豁免属性可读性与指令资格；
    /// helper 是该豁免的公共入口（PresentPhaseResolver 门与 CommandSourceEligibility 均经此判定）。
    /// </summary>
    [TestFixture]
    public sealed class PresentPhaseRevealHiddenAttributeReadoutTests
    {
        [Test]
        public void RevealHiddenService_True_WhenAudienceRevealHiddenSet()
        {
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.PresentationAudienceRevealHidden.Name] = true,
            };
            Assert.That(KnowledgeProjectionConsumer.IsAudienceRevealHidden(globals), Is.True,
                "reveal-hidden 受众的属性 HUD 不经 knowledge 门控（#1491 收割语义）。");
        }

        [Test]
        public void RevealHiddenService_False_WhenUnsetOrFalse()
        {
            Assert.That(KnowledgeProjectionConsumer.IsAudienceRevealHidden(null), Is.False);
            Assert.That(KnowledgeProjectionConsumer.IsAudienceRevealHidden(new Dictionary<string, object>()), Is.False,
                "knowledge projection 仍是普通受众属性 HUD 的唯一可读性权威（main 原合同不变）。");
            var off = new Dictionary<string, object> { [CoreServiceKeys.PresentationAudienceRevealHidden.Name] = false };
            Assert.That(KnowledgeProjectionConsumer.IsAudienceRevealHidden(off), Is.False);
        }
    }
}
