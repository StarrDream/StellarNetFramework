// Assets/StellarNetFramework/Shared/Serialization/ISerializer.cs

namespace StellarNet.Shared.Serialization
{
    // 框架统一序列化抽象接口。
    // 框架层只依赖此接口，不内置任何具体序列化实现。
    // 具体方案（MessagePack / MemoryPack / 自定义二进制）由开发者在装配阶段注入。
    // 职责边界：只负责协议体对象与 Payload 字节之间的转换。
    // NetworkEnvelope 头部封装格式属于 Adapter / 传输层职责，不属于此接口职责。
    public interface ISerializer
    {
        // 将协议体对象序列化为字节数组
        // 参数 obj：待序列化的协议体对象，不得为 null
        byte[] Serialize(object obj);

        // 将字节数组反序列化为指定类型的协议体对象
        // 参数 data：待反序列化的字节数组，不得为 null 或空
        // 参数 targetType：目标协议类型，必须继承自四协议基类之一
        object Deserialize(byte[] data, System.Type targetType);
    }
}