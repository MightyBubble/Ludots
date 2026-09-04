// 内核事件面交换原语(位置保持):把指定内核系统的私有 handler 从静态事件字段中
// 按原调用位替换为原生 handler / 移除 / 换回。静态字段不可 ref,故以传入-返回式
// API 落地。找不到目标 handler 即类型化抛错(接缝漂移必须显式失败,不静默双跑/漏跑)。
// 反射只读内核私有方法/字段,不改内核代码(内核是预言机)。

using System;
using System.Reflection;

namespace Sango.Runtime
{
    public static class SangoCityEventSwap
    {
        /// <summary>内核私有实例方法 → 可与事件字段比对/换回的委托。</summary>
        public static Delegate CreateKernelHandler<TDelegate>(object kernelSystem, string methodName)
            where TDelegate : Delegate
        {
            ArgumentNullException.ThrowIfNull(kernelSystem);
            MethodInfo? method = kernelSystem.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                throw new InvalidOperationException(
                    $"Kernel seam moved: '{kernelSystem.GetType().Name}.{methodName}' no longer exists; re-audit the D-1' event swap.");
            }

            return method.CreateDelegate(typeof(TDelegate), kernelSystem);
        }

        /// <summary>读内核私有静态字段(如 ClassicsCityWorking.CityBuildingTemplate)。</summary>
        public static object ReadKernelStaticField(Type kernelType, string fieldName)
        {
            FieldInfo? field = kernelType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Kernel seam moved: '{kernelType.Name}.{fieldName}' no longer exists; re-audit the D-1' event swap.");
            }

            return field.GetValue(null)!;
        }

        /// <summary>原位替换:invocation list 中 kernelHandler 处换 nativeHandler。</summary>
        public static Delegate SwapInto(Delegate? field, Delegate kernelHandler, Delegate nativeHandler)
        {
            ArgumentNullException.ThrowIfNull(kernelHandler);
            ArgumentNullException.ThrowIfNull(nativeHandler);
            if (field == null)
            {
                return nativeHandler;
            }

            Delegate[] invocation = field.GetInvocationList();
            Delegate? combined = null;
            bool found = false;
            foreach (Delegate entry in invocation)
            {
                bool isKernel = entry.Equals(kernelHandler);
                found |= isKernel;
                Delegate piece = isKernel ? nativeHandler : entry;
                combined = combined == null ? piece : Delegate.Combine(combined, piece);
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    $"Kernel handler '{kernelHandler.Method.DeclaringType?.Name}.{kernelHandler.Method.Name}' is not subscribed to the swapped event; the seam drifted.");
            }

            return combined!;
        }

        /// <summary>移除:摘掉 kernelHandler(复合收获链的 BuildingWorking 覆盖段)。</summary>
        public static Delegate? RemoveFrom(Delegate? field, Delegate kernelHandler)
        {
            ArgumentNullException.ThrowIfNull(kernelHandler);
            if (field == null)
            {
                return null;
            }

            Delegate[] invocation = field.GetInvocationList();
            Delegate? combined = null;
            foreach (Delegate entry in invocation)
            {
                if (entry.Equals(kernelHandler))
                {
                    continue;
                }

                combined = combined == null ? entry : Delegate.Combine(combined, entry);
            }

            return combined;
        }

        /// <summary>换回:nativeHandler 处还原 kernelHandler(Dispose)。</summary>
        public static Delegate SwapBack(Delegate? field, Delegate kernelHandler, Delegate nativeHandler)
        {
            ArgumentNullException.ThrowIfNull(kernelHandler);
            ArgumentNullException.ThrowIfNull(nativeHandler);
            if (field == null)
            {
                return kernelHandler;
            }

            Delegate[] invocation = field.GetInvocationList();
            Delegate? combined = null;
            foreach (Delegate entry in invocation)
            {
                Delegate piece = entry.Equals(nativeHandler) ? kernelHandler : entry;
                combined = combined == null ? piece : Delegate.Combine(combined, piece);
            }

            return combined!;
        }

        public static Delegate Combine(Delegate field, Delegate handler) => Delegate.Combine(field, handler)!;
    }
}
