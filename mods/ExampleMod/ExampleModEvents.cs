using Ludots.Core.Scripting;

namespace ExampleMod
{
    public static class ExampleModEvents
    {
        public static readonly EventKey SetSkillStep10Hz = new EventKey("ExampleMod.SetSkillStep10Hz");
        public static readonly EventKey SetSkillStep60Hz = new EventKey("ExampleMod.SetSkillStep60Hz");
    }
}

