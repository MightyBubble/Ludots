using System.Collections.Generic;

namespace Sango.Core
{
    public class TroopCompareFunction
    {
        public delegate int TroopCompare(Troop troop1, Troop troop2);
        static public Dictionary<string, TroopCompare> CompareMap = new Dictionary<string, TroopCompare>();

        static public void Register(string key, TroopCompare compare)
        {
            CompareMap[key] = compare;
        }
        static public TroopCompare Get(string name)
        {
            TroopCompare c;
            if (CompareMap.TryGetValue(name, out c))
                return c;
            Sango.Log.Error($"TroopCompareFunction中没有找到{name}的函数");
            return null;
        }

        static public void Init()
        {
            Register("command", (a, b) => a.Command.CompareTo(b.Command));
            Register("strength", (a, b) => a.Strength.CompareTo(b.Strength));
            Register("intelligence", (a, b) => a.Intelligence.CompareTo(b.Intelligence));
            Register("politics", (a, b) => a.Politics.CompareTo(b.Politics));
            Register("glamour", (a, b) => a.Glamour.CompareTo(b.Glamour));
            Register("age", (a, b) => a.Leader.Age.CompareTo(a.Leader.Age));
            Register("level", (a, b) => a.Leader.Level.Id.CompareTo(a.Leader.Level.Id));
            Register("spearLv", (a, b) => a.SpearLv.CompareTo(b.SpearLv));
            Register("halberdLv", (a, b) => a.HalberdLv.CompareTo(b.HalberdLv));
            Register("crossbowLv", (a, b) => a.CrossbowLv.CompareTo(b.CrossbowLv));
            Register("rideLv ", (a, b) => a.RideLv.CompareTo(b.RideLv));
            Register("waterLv", (a, b) => a.WaterLv.CompareTo(b.WaterLv));
            Register("machineLv", (a, b) => a.MachineLv.CompareTo(b.MachineLv));
            Register("official", (a, b) => SangoObject.Compare(a.Leader.Official, b.Leader.Official));
            Register("attack", (a, b) => a.Attack.CompareTo(b.Attack));
            Register("defence", (a, b) => a.Defence.CompareTo(b.Defence));
            Register("morale", (a, b) => a.morale.CompareTo(b.morale));
            Register("moveAbility", (a, b) => a.MoveAbility.CompareTo(b.MoveAbility));
            Register("troops", (a, b) => a.troops.CompareTo(b.troops));

        }
    }

}
