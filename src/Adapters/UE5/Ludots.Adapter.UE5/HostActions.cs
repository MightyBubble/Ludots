using System;
using System.Threading.Tasks;

namespace Ludots.Adapter.UE5
{
    /// <summary>宿主操作执行结果。</summary>
    public readonly record struct HostActionResult(
        bool Success,
        string ErrorMessage,
        object? Data = null)
    {
        public static HostActionResult Ok(object? data = null)
            => new(true, string.Empty, data);

        public static HostActionResult Fail(string errorMessage)
            => new(false, errorMessage ?? string.Empty);
    }

    /// <summary>
    /// 宿主操作抽象——Mod 通过 string action name 分发操作给宿主执行。
    /// <para>
    /// 宿主平台在启动时通过
    /// <c>engine.SetService(UE5AdapterServiceKeys.HostActions, impl)</c> 注入实现。
    /// </para>
    /// </summary>
    public interface IHostActions
    {
        /// <summary>
        /// 分发命名操作给宿主执行。结果统一通过 <paramref name="onCompleted"/> 回调返回。
        /// </summary>
        void Execute(string action, object? payload = null,
            Action<HostActionResult>? onCompleted = null);

        /// <summary>
        /// 异步版本：通过 Task 返回操作结果。
        /// </summary>
        Task<HostActionResult> ExecuteAsync(string action, object? payload = null)
        {
            var tcs = new TaskCompletionSource<HostActionResult>();
            Execute(action, payload, r => tcs.TrySetResult(r));
            return tcs.Task;
        }
    }
}