/*
 * 版权属于：yitter(yitter@126.com)
 * 开源地址：https://github.com/yitter/idgenerator
 * 版权协议：MIT
 * 版权说明：只要保留本版权，你可以免费使用、修改、分发本代码。
 * 免责条款：任何因为本代码产生的系统、法律、政治、宗教问题，均与版权所有者无关。
 * 
 */

// Yitter.IdGenerator

namespace Yitter.IdGenerator;

/// <summary>
/// 这是一个调用的例子，默认情况下，单机集成者可以直接使用 NextId()。
/// </summary>
public class SnowflakeGenerator
{
    private static IIdGenerator _IdGenInstance = null;

    public static IIdGenerator IdGenInstance => _IdGenInstance;

    /// <summary>
    /// 生成新的Id
    /// 调用本方法前，请确保调用了 SetIdGenerator 方法做初始化。
    /// </summary>
    /// <returns></returns>
    public static long NextId()
    {
        /// <summary>
        /// 设置参数，建议程序初始化时执行一次
        /// </summary>
        /// <param name="options"></param>
        if (_IdGenInstance is null)
        {
            var r = new Random();
            var @int = r.Next(0, 63);
            _IdGenInstance = new DefaultIdGenerator(new IdGeneratorOptions(Convert.ToUInt16(@int)));
        }

        return _IdGenInstance.NewLong();
    }
}
