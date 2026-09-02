namespace Sango.Core
{
    /// <summary>
    /// 数组数据对象接口
    /// </summary>
    public interface IAarryDataObject
    {
        /// <summary>
        /// 从整型数组创建数据对象
        /// </summary>
        /// <param name="values">整型数组</param>
        /// <returns>创建的数据对象</returns>
        public abstract IAarryDataObject FromArray(int[] values);
        
        /// <summary>
        /// 将数据对象转换为整型数组
        /// </summary>
        /// <returns>转换后的整型数组</returns>
        public abstract int[] ToArray();
    }
}
