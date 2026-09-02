using System;

namespace Sango.Core
{
    /// <summary>
    /// 模块属性,定义模块的执行优先级 order越小越优先 以及模块的别名
    /// </summary>
    public class GameSystemAttribute : Attribute
    {
        /// <summary>
        /// 排序标志, 越小越前面, nickName相同则只会创建order最大的
        /// </summary>
        public int order;

        /// <summary>
        /// 别名
        /// </summary>
        public string nickName;

        /// <summary>
        /// 是否自动初始化
        /// </summary>
        public bool autoInit = true;
    }
}
