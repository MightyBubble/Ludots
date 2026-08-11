using Ludots.Core.Scripting;

namespace Y5kGrandStrategyMod.Runtime;

public static class Y5kDemoServiceKeys
{
	public static readonly ServiceKey<Y5kDemoState> State = new("Y5kDemoState");
	public static readonly ServiceKey<Y5kLoopDemoDirectorSystem> Director = new("Y5kLoopDemoDirector");
}
