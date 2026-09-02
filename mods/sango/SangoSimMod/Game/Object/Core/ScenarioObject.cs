/*
 * 文件名：ScenarioObject.cs
 * 描述：剧本对象抽象基类，用于延迟加载游戏对象
 * 创建日期：2026-03-27
 * 最后修改：2026-03-27
 */

using System.IO;

namespace Sango.Core
{
    /// <summary>
    /// 剧本对象抽象基类，用于延迟加载游戏对象
    /// 通过ID引用对象，在需要时才实际获取对象实例
    /// </summary>
    /// <typeparam name="T">游戏对象类型</typeparam>
    public abstract class ScenarioObject<T> where T : SangoObject, new()
    {
        /// <summary>
        /// 对象ID缓存
        /// </summary>
        protected int _id = 0;

        /// <summary>
        /// 对象实例缓存
        /// </summary>
        protected T _obj = null;

        /// <summary>
        /// 获取或设置对象ID
        /// 设置ID时会清除对象实例缓存
        /// </summary>
        public int Id
        {
            get
            {
                if (_obj != null)
                {
                    return _obj.Id;
                }
                else { return _id; }
            }
            set
            {
                if (_id != value)
                {
                    _id = value;
                    _obj = null;
                }
            }
        }

        /// <summary>
        /// 获取或设置对象实例
        /// 获取时会根据ID延迟加载对象
        /// </summary>
        public T Object
        {
            get
            {
                if (_id > 0)
                {
                    _obj = Get(_id);
                    if (_obj == null)
                    {
                        // 报错
                    }
                    _id = 0;
                }
                return _obj;
            }
            set
            {
                _obj = value;
                _id = 0;
            }
        }

        /// <summary>
        /// 检查对象是否有效
        /// </summary>
        /// <returns>对象ID是否大于0</returns>
        public bool IsValid() { return Id > 0; }

        /// <summary>
        /// 根据ID获取对象实例，子类需要实现此方法
        /// </summary>
        /// <param name="id">对象ID</param>
        /// <returns>对象实例</returns>
        protected virtual T Get(int id) { return null; }

        /// <summary>
        /// 隐式转换为对象实例
        /// </summary>
        /// <param name="d">剧本对象</param>
        public static implicit operator T(ScenarioObject<T> d)
        {
            return (d == null) ? null : d.Object;
        }

        /// <summary>
        /// 显式转换为对象ID
        /// </summary>
        /// <param name="d">剧本对象</param>
        public static explicit operator int(ScenarioObject<T> d)
        {
            return d.Id;
        }
    }
}
