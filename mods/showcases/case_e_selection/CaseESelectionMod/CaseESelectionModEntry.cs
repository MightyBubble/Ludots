using Ludots.Core.Modding;

namespace CaseESelectionMod;

/// <summary>
/// Case E (#1398 S3) 纯数据 showcase：框选全链由配置资产拼装，本 mod 不含业务代码，
/// 空入口仅为满足 mod 装载合同（mod.json main 指向本程序集）。
/// </summary>
public sealed class CaseESelectionModEntry : IMod
{
    public void OnLoad(IModContext context) { }
    public void OnUnload() { }
}
