using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sango.Core
{
    /// <summary>
    /// 字符串数据接口
    /// </summary>
    public interface IDataString
    {
        /// <summary>
        /// 将数据对象转换为字符串
        /// </summary>
        /// <returns>转换后的字符串</returns>
        public string ToString();
        
        /// <summary>
        /// 从字符串加载数据到对象
        /// </summary>
        /// <param name="data">要加载的字符串数据</param>
        public void FromString(string data);
    }
}
