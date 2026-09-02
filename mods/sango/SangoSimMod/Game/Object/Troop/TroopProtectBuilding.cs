using System;

namespace Sango.Core
{
    public class TroopProtectBuilding : TroopMissionBehaviour
    {
        public override MissionType MissionType { get { return MissionType.TroopProtectBuilding; } }
        public override bool IsMissionComplete => throw new NotImplementedException();

        public override bool DoAI(Troop troop, Scenario scenario)
        {
            return true;
        }
    }
}
