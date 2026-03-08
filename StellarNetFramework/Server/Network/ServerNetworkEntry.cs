using StellarNet.Server.Adapter;
using StellarNet.Server.Session;
using StellarNet.Shared.Envelope;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Server.Network
{
    /// <summary>
    /// 服务端统一网络入口，是所有客户端上行消息进入框架的第一个处理节点。
    /// 职责严格限定为：接收 Adapter 上抛的 NetworkEnvelope、查表反序列化、判断域归属、转发给对应路由器。
    /// 禁止在此层编写任何业务逻辑、修改业务状态或直接抛领域事件。
    /// 服务端入站链收到 S2C 协议时必须直接阻断，防止方向非法的协议进入业务处理链。
    /// </summary>
    public sealed class ServerNetworkEntry
    {
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly ServerGlobalMessageRouter _globalRouter;
        private readonly RoomOwnershipValidator _roomOwnershipValidator;
        private readonly SessionManager _sessionManager;

        public ServerNetworkEntry(
            MessageRegistry messageRegistry,
            ISerializer serializer,
            ServerGlobalMessageRouter globalRouter,
            RoomOwnershipValidator roomOwnershipValidator,
            SessionManager sessionManager)
        {
            if (messageRegistry == null)
            {
                Debug.LogError("[ServerNetworkEntry] 构造失败：messageRegistry 为 null。");
                return;
            }

            if (serializer == null)
            {
                Debug.LogError("[ServerNetworkEntry] 构造失败：serializer 为 null。");
                return;
            }

            if (globalRouter == null)
            {
                Debug.LogError("[ServerNetworkEntry] 构造失败：globalRouter 为 null。");
                return;
            }

            if (roomOwnershipValidator == null)
            {
                Debug.LogError("[ServerNetworkEntry] 构造失败：roomOwnershipValidator 为 null。");
                return;
            }

            if (sessionManager == null)
            {
                Debug.LogError("[ServerNetworkEntry] 构造失败：sessionManager 为 null。");
                return;
            }

            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _globalRouter = globalRouter;
            _roomOwnershipValidator = roomOwnershipValidator;
            _sessionManager = sessionManager;
        }

        /// <summary>
        /// 绑定到 Adapter 的数据接收事件，由 GlobalInfrastructure 在装配阶段调用。
        /// </summary>
        public void BindToAdapter(MirrorServerAdapter adapter)
        {
            if (adapter == null)
            {
                Debug.LogError("[ServerNetworkEntry] BindToAdapter 失败：adapter 为 null。");
                return;
            }

            adapter.OnDataReceived += OnEnvelopeReceived;
        }

        /// <summary>
        /// 解绑 Adapter 事件，由 GlobalInfrastructure 在 Shutdown 阶段调用。
        /// </summary>
        public void UnbindFromAdapter(MirrorServerAdapter adapter)
        {
            if (adapter == null)
            {
                return;
            }

            adapter.OnDataReceived -= OnEnvelopeReceived;
        }

        /// <summary>
        /// 处理来自 Adapter 的 NetworkEnvelope。
        /// 执行顺序：查表 → 反序列化 → 方向校验 → 域分流 → 转发。
        /// ConnectionId 在此处作为运行时上下文随消息一起传递，不写入 NetworkEnvelope 字段。
        /// </summary>
        private void OnEnvelopeReceived(ConnectionId connectionId, Shared.Envelope.NetworkEnvelope envelope)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError($"[ServerNetworkEntry] 收到无效 ConnectionId 的消息，MessageId={envelope?.MessageId}，已丢弃。");
                return;
            }

            if (envelope == null)
            {
                Debug.LogError($"[ServerNetworkEntry] 收到 null envelope，ConnectionId={connectionId}，已丢弃。");
                return;
            }

            // 通过 MessageRegistry 查找协议元数据，查不到则视为未注册协议，直接阻断
            var metadata = _messageRegistry.GetByMessageId(envelope.MessageId);
            if (metadata == null)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 未知 MessageId={envelope.MessageId}，ConnectionId={connectionId}，协议未在 MessageRegistry 中注册，已丢弃。");
                return;
            }

            // 方向校验：服务端入站链收到 S2C 协议必须直接阻断，防止方向非法的协议污染业务链
            if (metadata.Direction == MessageDirection.S2C)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 非法协议方向：服务端入站链收到 S2C 协议，MessageId={envelope.MessageId}，Type={metadata.MessageType?.Name}，ConnectionId={connectionId}，已阻断。");
                return;
            }

            // 反序列化协议体
            object message = _serializer.Deserialize(envelope.Payload, metadata.MessageType);
            if (message == null)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 协议反序列化失败，MessageId={envelope.MessageId}，Type={metadata.MessageType?.Name}，ConnectionId={connectionId}，已丢弃。");
                return;
            }

            // 域分流：根据四协议基类推导的域归属决定转发目标
            // 全局域协议不进入房间归属一致性校验链
            // 房间域协议必须进入房间归属一致性校验链
            if (metadata.Domain == MessageDomain.Global)
            {
                _globalRouter.Dispatch(connectionId, metadata, message);
            }
            else if (metadata.Domain == MessageDomain.Room)
            {
                // 只有确认协议属于 C2SRoomMessage 之后，才允许进入房间归属一致性校验链
                _roomOwnershipValidator.Validate(connectionId, envelope.RoomId, metadata, message);
            }
        }
    }
}