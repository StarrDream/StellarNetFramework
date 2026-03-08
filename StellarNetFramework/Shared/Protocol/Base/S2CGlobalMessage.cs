namespace StellarNet.Shared.Protocol
{
    /// <summary>
    /// 服务端下行全局域协议基类。
    /// 继承此基类表示：消息方向为 S2C，所属域为 Global。
    /// 全局域协议不依赖当前房间运行时归属上下文，可在任意客户端状态下按过滤器规则接收。
    /// 典型场景：登录结果、重连结果、公告推送、全局系统通知等。
    /// ExtData 用于兼容性扩展承载，不应作为常规业务字段的替代方案。
    /// </summary>
    public abstract class S2CGlobalMessage
    {
        /// <summary>
        /// 扩展数据字段，用于承载业务层在特定场景下需要附加的自定义序列化数据。
        /// 不应用于代替正常的强类型协议字段设计。
        /// </summary>
        public byte[] ExtData;
    }
}