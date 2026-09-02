namespace Sango.Core
{
    /// <summary>
    /// 字符串数据对象接口
    /// </summary>
    public interface IStringDataObject
    {
        /// <summary>
        /// 从字符串创建数据对象
        /// </summary>
        /// <param name="content">字符串内容</param>
        /// <returns>创建的数据对象</returns>
        public abstract IStringDataObject FromString(string content);
    }
}
