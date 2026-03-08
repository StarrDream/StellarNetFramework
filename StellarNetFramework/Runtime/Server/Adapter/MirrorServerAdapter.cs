using System;
using Mirror;
using StellarNet.Shared.Envelope;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Server.Adapter
{
    /// <summary>
    /// Mirror 服务端网络适配器，负责框架与 Mirror 底层网络库的边界隔离。
    /// 只使用 Mirror 的基础通讯能力：连接建立/断开事件与字节收发，严禁使用 SyncVar、NetworkBehaviour 等高级特性。
    /// Mirror 原生连接句柄（connectionId int）在此层统一映射为框架 ConnectionId 值类型。
    /// 上层通过订阅事件接收连接通知与数据包，Adapter 不处理任何业务状态。
    /// Adapter 发送入口只负责底层传输，不负责房间业务语义决策。
    /// </summary>
    public sealed class MirrorServerAdapter : NetworkManager
    {
        // ── 上抛事件，上层通过订阅这些事件接收通知 ──────────────────

        /// <summary>
        /// 新客户端连接建立事件，参数为框架统一 ConnectionId。
        /// </summary>
        public event Action<ConnectionId> OnClientConnected;

        /// <summary>
        /// 客户端连接断开事件，参数为框架统一 ConnectionId。
        /// </summary>
        public event Action<ConnectionId> OnClientDisconnected;

        /// <summary>
        /// 收到客户端数据包事件，参数为（框架 ConnectionId，NetworkEnvelope）。
        /// ConnectionId 属于服务端接收链的运行时上下文，由此处注入，不属于 NetworkEnvelope 字段。
        /// </summary>
        public event Action<ConnectionId, NetworkEnvelope> OnDataReceived;

        private ISerializer _serializer;

        // 框架自定义消息 ID，用于在 Mirror 消息系统中注册字节透传通道
        // 使用 ushort 类型以符合 Mirror 消息 ID 规范
        private const ushort FrameworkMessageId = 9999;

        /// <summary>
        /// 由 GlobalInfrastructure 在装配阶段调用，注入序列化器依赖。
        /// 不在 Awake/Start 中自行初始化，遵循统一装配原则。
        /// </summary>
        public void Initialize(ISerializer serializer)
        {
            if (serializer == null)
            {
                Debug.LogError($"[MirrorServerAdapter] Initialize 失败：物体 {name} 注入的 serializer 为 null，Adapter 将无法正常工作。");
                return;
            }

            _serializer = serializer;
        }

        /// <summary>
        /// 启动服务端监听。由 GlobalInfrastructure 在装配完成后显式调用。
        /// </summary>
        public void StartListening()
        {
            if (_serializer == null)
            {
                Debug.LogError($"[MirrorServerAdapter] StartListening 失败：物体 {name} 的 _serializer 为 null，请先调用 Initialize()。");
                return;
            }

            // 注册框架自定义字节透传消息处理器
            // 使用 Mirror 的 NetworkServer.RegisterHandler 注册原始字节消息
            NetworkServer.RegisterHandler<FrameworkRawMessage>(OnMirrorMessageReceived);
            StartServer();
            Debug.Log($"[MirrorServerAdapter] 服务端开始监听，物体：{name}。");
        }

        /// <summary>
        /// 停止服务端监听。由 GlobalInfrastructure 在 Shutdown 阶段显式调用。
        /// </summary>
        public void StopListening()
        {
            NetworkServer.UnregisterHandler<FrameworkRawMessage>();
            StopServer();
            Debug.Log($"[MirrorServerAdapter] 服务端已停止监听，物体：{name}。");
        }

        /// <summary>
        /// 向指定连接发送 NetworkEnvelope。
        /// 此方法只负责底层传输，不负责房间业务语义决策、目标集合解析或 RoomId 补全。
        /// 调用方（ServerSendCoordinator）必须在调用此方法前完成所有上下文绑定。
        /// </summary>
        public void Send(ConnectionId connectionId, NetworkEnvelope envelope)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError($"[MirrorServerAdapter] Send 失败：ConnectionId 无效，当前值：{connectionId}，MessageId：{envelope?.MessageId}。");
                return;
            }

            if (envelope == null)
            {
                Debug.LogError($"[MirrorServerAdapter] Send 失败：envelope 为 null，ConnectionId：{connectionId}。");
                return;
            }

            if (!NetworkServer.connections.TryGetValue(connectionId.Value, out var conn))
            {
                Debug.LogError($"[MirrorServerAdapter] Send 失败：在 Mirror 连接表中找不到 ConnectionId={connectionId}，MessageId：{envelope.MessageId}。");
                return;
            }

            byte[] envelopeBytes = _serializer.Serialize(envelope);
            if (envelopeBytes == null)
            {
                Debug.LogError($"[MirrorServerAdapter] Send 失败：NetworkEnvelope 序列化结果为 null，ConnectionId：{connectionId}，MessageId：{envelope.MessageId}。");
                return;
            }

            var rawMsg = new FrameworkRawMessage { Data = envelopeBytes };
            conn.Send(rawMsg);
        }

        // ── Mirror 生命周期回调重写 ──────────────────────────────────

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            // 将 Mirror 原生 connectionId 映射为框架统一 ConnectionId，上层不得直接依赖 Mirror 原生类型
            var connectionId = new ConnectionId(conn.connectionId);
            Debug.Log($"[MirrorServerAdapter] 客户端连接建立，ConnectionId={connectionId}。");
            OnClientConnected?.Invoke(connectionId);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            var connectionId = new ConnectionId(conn.connectionId);
            Debug.Log($"[MirrorServerAdapter] 客户端连接断开，ConnectionId={connectionId}。");
            OnClientDisconnected?.Invoke(connectionId);
            // 调用基类确保 Mirror 内部连接清理正常执行
            base.OnServerDisconnect(conn);
        }

        /// <summary>
        /// 接收 Mirror 底层字节消息，解封装为 NetworkEnvelope 后上抛给框架核心层。
        /// Adapter 不解释业务层 RoomId 参数语义，不决定房间业务路由目标。
        /// </summary>
        private void OnMirrorMessageReceived(NetworkConnectionToClient conn, FrameworkRawMessage rawMsg)
        {
            if (rawMsg.Data == null || rawMsg.Data.Length == 0)
            {
                Debug.LogError($"[MirrorServerAdapter] 收到空数据包，ConnectionId={conn.connectionId}，已丢弃。");
                return;
            }

            if (_serializer == null)
            {
                Debug.LogError($"[MirrorServerAdapter] _serializer 为 null，无法解封装数据包，ConnectionId={conn.connectionId}，已丢弃。");
                return;
            }

            var envelope = _serializer.Deserialize<NetworkEnvelope>(rawMsg.Data);
            if (envelope == null)
            {
                Debug.LogError($"[MirrorServerAdapter] NetworkEnvelope 反序列化失败，ConnectionId={conn.connectionId}，已丢弃。");
                return;
            }

            var connectionId = new ConnectionId(conn.connectionId);
            OnDataReceived?.Invoke(connectionId, envelope);
        }

        /// <summary>
        /// Mirror 自定义消息结构，用于在 Mirror 消息系统中透传框架字节数据。
        /// 此结构只存在于 Adapter 层，上层不感知 Mirror 消息类型。
        /// </summary>
        private struct FrameworkRawMessage : NetworkMessage
        {
            public byte[] Data;
        }
    }
}
