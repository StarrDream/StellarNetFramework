// Assets/StellarNetFramework/Server/Network/Adapter/INetworkAdapter.cs

using System;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.Envelope;
using StellarNet.Shared.Enums;

namespace StellarNet.Server.Network.Adapter
{
    // 框架统一网络 Adapter 抽象接口，与底层网络库（Mirror）解耦。
    // 采用事件委托上抛模式，不采用回调注入模式，不采用继承重写作为主扩展方式。
    // 上层通过订阅事件接收连接建立、断开与收包通知。
    // Adapter 只面向连接级发送，不提供"向整个房间发送"的房间语义能力。
    // 禁止职责：
    //   不处理任何业务状态，不决定房间路由目标，不缓存房间内部数据，
    //   不进行玩法权限判断，不直接触发业务组件逻辑，
    //   不通过命名、字段内容或自定义规则重新判定协议方向与域归属。
    public interface INetworkAdapter
    {
        // 底层连接建立事件，Adapter 完成 ConnectionId 映射后上抛
        // 参数：框架统一 ConnectionId
        event Action<ConnectionId> OnConnected;

        // 底层连接断开事件
        // 参数1：框架统一 ConnectionId
        // 参数2：断开原因描述，用于日志诊断
        event Action<ConnectionId, string> OnDisconnected;

        // 底层收包事件，Adapter 完成字节流解封装为 NetworkEnvelope 后上抛
        // 参数1：框架统一 ConnectionId，来源于底层连接上下文，不由客户端上传
        // 参数2：解封装后的 NetworkEnvelope
        event Action<ConnectionId, NetworkEnvelope> OnDataReceived;

        // 启动 Adapter，开始监听连接
        void Start();

        // 停止 Adapter，停止接收新连接，由 GlobalInfrastructure.Shutdown() 最后调用
        void Stop();

        // 向指定连接发送已完成上下文绑定的 NetworkEnvelope。
        // 调用方（发送器）必须在调用此方法前完成：
        //   协议序列化、MessageId 解析、投递语义确定与运行时上下文绑定。
        // Adapter 不负责补全缺失的 RoomId，不负责从业务消息对象推导 MessageId。
        // 参数 deliveryMode：由发送器从 MessageRegistry 查询后传入，Adapter 映射到底层网络库发送方式
        void Send(ConnectionId connectionId, NetworkEnvelope envelope, DeliveryMode deliveryMode);

        // 断开指定连接，用于会话接管时强制踢出旧连接
        void Disconnect(ConnectionId connectionId);
    }
}