using System.Collections.Generic;

namespace Sango.Core
{
    public class PersonFunctions
    {
        public delegate int PersonCompare(Person person1, Person person2);
        public delegate int PersonGet(Person person);
        static public Dictionary<string, PersonCompare> CompareMap = new Dictionary<string, PersonCompare>();
        static public Dictionary<string, PersonGet> GetMap = new Dictionary<string, PersonGet>();

        static public void Register(string key, PersonCompare compare)
        {
            CompareMap[key] = compare;
        }

        static public void Register(string key, PersonGet compare)
        {
            GetMap[key] = compare;
        }
        static public PersonCompare CompareGet(string name)
        {
            PersonCompare c;
            if (CompareMap.TryGetValue(name, out c))
                return c;
            return null;
        }
        static public PersonGet GetGet(string name)
        {
            PersonGet c;
            if (GetMap.TryGetValue(name, out c))
                return c;
            return null;
        }

        static public void Init()
        {
            Register("command", (a, b) => a.Command.CompareTo(b.Command));
            Register("strength", (a, b) => a.Strength.CompareTo(b.Strength));
            Register("intelligence", (a, b) => a.Intelligence.CompareTo(b.Intelligence));
            Register("politics", (a, b) => a.Politics.CompareTo(b.Politics));
            Register("glamour", (a, b) => a.Glamour.CompareTo(b.Glamour));
            Register("age", (a, b) => a.Age.CompareTo(b.Age));
            Register("level", (a, b) => a.Level.Id.CompareTo(b.Level.Id));
            Register("spearLv", (a, b) => a.SpearLv.CompareTo(b.SpearLv));
            Register("halberdLv", (a, b) => a.HalberdLv.CompareTo(b.HalberdLv));
            Register("crossbowLv", (a, b) => a.CrossbowLv.CompareTo(b.CrossbowLv));
            Register("rideLv ", (a, b) => a.RideLv.CompareTo(b.RideLv));
            Register("waterLv", (a, b) => a.WaterLv.CompareTo(b.WaterLv));
            Register("machineLv", (a, b) => a.MachineLv.CompareTo(b.MachineLv));
            Register("official", (a, b) => SangoObject.Compare(a.Official, b.Official));

            Register("command", (a) => a.Command);
            Register("strength", (a) => a.Strength);
            Register("intelligence", (a) => a.Intelligence);
            Register("politics", (a) => a.Politics);
            Register("glamour", (a) => a.Glamour);
            Register("age", (a) => a.Age);
            Register("level", (a, b) => a.Level.Id);
            Register("spearLv", (a) => a.SpearLv);
            Register("halberdLv", (a) => a.HalberdLv);
            Register("crossbowLv", (a) => a.CrossbowLv);
            Register("rideLv ", (a) => a.RideLv);
            Register("waterLv", (a) => a.WaterLv);
            Register("machineLv", (a) => a.MachineLv);
            Register("official", (a) => a.Official.Id);
        }
    }

}
