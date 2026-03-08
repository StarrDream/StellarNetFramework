namespace StellarNet.Shared.Serialization
{
    /// <summary>
    /// 序列化抽象接口，定义框架层统一的序列化与反序列化契约。
    /// 具体实现由各端自行提供（框架默认提供 NewtonsoftJsonSerializer）。
    /// 禁止在 Shared 层承载任何具体序列化实现，只承载此抽象接口定义。
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// 将对象序列化为字节数组。
        /// 序列化失败时实现方应输出详细 Error 日志并返回 null，不得抛出异常掩盖错误。
        /// </summary>
        /// <param name="obj">待序列化的对象，不允许为 null。</param>
        byte[] Serialize(object obj);

        /// <summary>
        /// 将字节数组反序列化为指定类型的对象实例。
        /// 反序列化失败时实现方应输出详细 Error 日志并返回 null，不得抛出异常掩盖错误。
        /// </summary>
        /// <param name="data">待反序列化的字节数组，不允许为 null 或空数组。</param>
        /// <param name="targetType">目标类型，来源于 MessageRegistry 查表结果。</param>
        object Deserialize(byte[] data, System.Type targetType);

        /// <summary>
        /// 泛型反序列化重载，方便调用方直接获取强类型结果。
        /// </summary>
        T Deserialize<T>(byte[] data);
    }
}