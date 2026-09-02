/*
 * 文件名：System.cs
 * 描述：系统基类，提供单例模式实现
 * 创建日期：2026-03-27
 * 最后修改：2026-03-27
 */

namespace Sango
{
    /// <summary>
    /// 系统基类，提供单例模式实现
    /// </summary>
    /// <typeparam name="T">系统类型</typeparam>
    public abstract class System<T> : Module where T : Module, new()
    {
        /// <summary>
        /// 实例
        /// </summary>
        private static T _instance;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new T();
                }
                return _instance;
            }
        }
    }

}
