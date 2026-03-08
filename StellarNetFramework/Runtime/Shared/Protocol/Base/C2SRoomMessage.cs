namespace StellarNet.Shared.Protocol
{
    /// <summary>
    /// 客户端上行房间域协议基类。
    /// 继承此基类表示：消息方向为 C2S，所属域为 Room。
    /// 房间域协议必须绑定有效的房间运行时上下文，进入服务端房间归属一致性校验链。
    /// 典型场景：房间业务组件请求、局内操作、准备状态变更等。
    /// ExtData 用于兼容性扩展承载，不应作为常规业务字段的替代方案。
    /// </summary>
    public abstract class C2SRoomMessage
    {
        /// <summary>
        /// 扩展数据字段，用于承载业务层在特定场景下需要附加的自定义序列化数据。
        /// 不应用于代替正常的强类型协议字段设计。
        /// </summary>
        public byte[] ExtData;
    }
}