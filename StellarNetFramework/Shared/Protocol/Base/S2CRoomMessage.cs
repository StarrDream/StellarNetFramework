namespace StellarNet.Shared.Protocol
{
    /// <summary>
    /// 服务端下行房间域协议基类。
    /// 继承此基类表示：消息方向为 S2C，所属域为 Room。
    /// 房间域协议必须保留可读取的房间运行时上下文，用于状态过滤、回放隔离与延迟消息污染拦截。
    /// 典型场景：房间业务组件广播、局内公共状态变化、房间内单体私有辅助消息等。
    /// ExtData 用于兼容性扩展承载，不应作为常规业务字段的替代方案。
    /// </summary>
    public abstract class S2CRoomMessage
    {
        /// <summary>
        /// 扩展数据字段，用于承载业务层在特定场景下需要附加的自定义序列化数据。
        /// 不应用于代替正常的强类型协议字段设计。
        /// </summary>
        public byte[] ExtData;
    }
}