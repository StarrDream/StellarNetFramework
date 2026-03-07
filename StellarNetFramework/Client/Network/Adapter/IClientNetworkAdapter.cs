// Assets/StellarNetFramework/Client/Network/Adapter/IClientNetworkAdapter.cs

using System;
using StellarNet.Shared.Protocol.Envelope;
using StellarNet.Shared.Enums;

namespace StellarNet.Client.Network.Adapter
{
    // 客户端统一网络 Adapter 抽象接口，与底层网络库（Mirror）解耦。
    // 采用事件委托上抛模式，上层通过订阅事件接收连接建立、断开与收包通知。
    // 客户端 Adapter 只面向单连接（客户端同一时刻只持有一条服务端连接），
    // 不提供多连接管理能力，不提供房间语义能力。
    // 禁止职责：
    //   不处理任何业务状态，不决定协议路由目标，不缓存会话数据，
    //   不进行玩法权限判断，不直接触发业务组件逻辑。
    public interface IClientNetworkAdapter
    {
        // 连接建立事件，底层握手完成后上抛
        event Action OnConnected;

        // 连接断开事件
        // 参数：断开原因描述，用于日志诊断
        event Action<string> OnDisconnected;

        // 收包事件，Adapter 完成字节流解封装为 NetworkEnvelope 后上抛
        event Action<NetworkEnvelope> OnDataReceived;

        // 发起连接请求
        // 参数 host：服务端地址
        // 参数 port：服务端端口
        void Connect(string host, int port);

        // 主动断开连接
        void Disconnect();

        // 发送已完成上下文绑定的 NetworkEnvelope
        // 参数 deliveryMode：由发送器从 MessageRegistry 查询后传入，Adapter 映射到底层发送通道
        void Send(NetworkEnvelope envelope, DeliveryMode deliveryMode);

        // 当前是否已连接
        bool IsConnected { get; }
    }
}