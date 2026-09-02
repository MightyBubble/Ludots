using System;

namespace Sango.Core
{
    public class TroopProtectTroop : TroopMissionBehaviour
    {
        public override MissionType MissionType { get { return MissionType.TroopProtectTroop; } }
        public override bool IsMissionComplete => throw new NotImplementedException();

        public override bool DoAI(Troop troop, Scenario scenario)
        {
            return true;
        }
    }
}
