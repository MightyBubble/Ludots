namespace Ludots.Core.Hosting
{
    /// <summary>
    /// Shell 会话内向 mod 广播的启动器站点地址：CEF 表面导航目标（环回 HTTP 上的 React launcher）。
    /// 由宿主在 Compose 后、Loop 前经引擎服务注入。
    /// </summary>
    public sealed record LauncherShellSite(string BaseUrl)
    {
        public const string ServiceKeyName = "LauncherShell.Site";
    }
}
