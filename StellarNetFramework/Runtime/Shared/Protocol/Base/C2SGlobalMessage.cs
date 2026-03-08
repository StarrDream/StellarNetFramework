namespace StellarNet.Shared.Protocol
{
    /// <summary>
    /// 客户端上行全局域协议基类。
    /// 继承此基类表示：消息方向为 C2S，所属域为 Global。
    /// 全局域协议不进入房间归属一致性校验链，不依赖当前房间运行时上下文。
    /// 典型场景：登录、建房、加房请求、重连认证、公告拉取、全局聊天等。
    /// ExtData 用于兼容性扩展承载，不应作为常规业务字段的替代方案。
    /// </summary>
    public abstract class C2SGlobalMessage
    {
        /// <summary>
        /// 扩展数据字段，用于承载业务层在特定场景下需要附加的自定义序列化数据。
        /// 不应用于代替正常的强类型协议字段设计。
        /// </summary>
        public byte[] ExtData;
    }
}