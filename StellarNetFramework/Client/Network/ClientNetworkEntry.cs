using StellarNet.Client.Adapter;
using StellarNet.Client.Session;
using StellarNet.Client.State;
using StellarNet.Shared.Envelope;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Client.Network
{
    /// <summary>
    /// 客户端统一网络入口，是所有服务端下行消息进入框架的第一个处理节点。
    /// 职责严格限定为：接收 Adapter 上抛的 NetworkEnvelope、查表反序列化、方向校验、状态过滤、域分流、转发给对应路由器。
    /// 引入 ClientStateProtocolFilter 进行前置拦截，防止非法状态迁移与延迟消息污染。
    /// 修正：不再持有静态 ClientRoomMessageRouter，而是从 GlobalClientManager 动态获取当前在线房间的 Router。
    /// </summary>
    public sealed class ClientNetworkEntry
    {
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly ClientGlobalMessageRouter _globalRouter;
        private readonly GlobalClientManager _globalClientManager; // 替换原有的静态 RoomRouter
        private readonly ClientSessionContext _sessionContext;
        private readonly ClientStateProtocolFilter _protocolFilter;

        /// <summary>
        /// 当前网络入口是否处于可用状态。
        /// </summary>
        public bool IsAvailable =>
            _messageRegistry != null &&
            _serializer != null &&
            _globalRouter != null &&
            _globalClientManager != null &&
            _sessionContext != null &&
            _protocolFilter != null;

        public ClientNetworkEntry(
            MessageRegistry messageRegistry,
            ISerializer serializer,
            ClientGlobalMessageRouter globalRouter,
            GlobalClientManager globalClientManager,
            ClientSessionContext sessionContext,
            ClientStateProtocolFilter protocolFilter)
        {
            if (messageRegistry == null)
            {
                Debug.LogError("[ClientNetworkEntry] 构造失败：messageRegistry 为 null。");
                return;
            }

            if (serializer == null)
            {
                Debug.LogError("[ClientNetworkEntry] 构造失败：serializer 为 null。");
                return;
            }

            if (globalRouter == null)
            {
                Debug.LogError("[ClientNetworkEntry] 构造失败：globalRouter 为 null。");
                return;
            }

            if (globalClientManager == null)
            {
                Debug.LogError("[ClientNetworkEntry] 构造失败：globalClientManager 为 null。");
                return;
            }

            if (sessionContext == null)
            {
                Debug.LogError("[ClientNetworkEntry] 构造失败：sessionContext 为 null。");
                return;
            }

            if (protocolFilter == null)
            {
                Debug.LogError("[ClientNetworkEntry] 构造失败：protocolFilter 为 null。");
                return;
            }

            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _globalRouter = globalRouter;
            _globalClientManager = globalClientManager;
            _sessionContext = sessionContext;
            _protocolFilter = protocolFilter;
        }

        public void BindToAdapter(MirrorClientAdapter adapter)
        {
            if (!IsAvailable)
            {
                Debug.LogError("[ClientNetworkEntry] BindToAdapter 失败：当前对象未完成有效初始化。");
                return;
            }

            if (adapter == null)
            {
                Debug.LogError("[ClientNetworkEntry] BindToAdapter 失败：adapter 为 null。");
                return;
            }

            adapter.OnDataReceived += OnEnvelopeReceived;
        }

        public void UnbindFromAdapter(MirrorClientAdapter adapter)
        {
            if (adapter == null)
            {
                return;
            }

            adapter.OnDataReceived -= OnEnvelopeReceived;
        }

        private void OnEnvelopeReceived(NetworkEnvelope envelope)
        {
            if (!IsAvailable)
            {
                Debug.LogError("[ClientNetworkEntry] OnEnvelopeReceived 失败：当前对象未完成有效初始化。");
                return;
            }

            if (envelope == null)
            {
                Debug.LogError("[ClientNetworkEntry] 收到 null envelope，已丢弃。");
                return;
            }

            var metadata = _messageRegistry.GetByMessageId(envelope.MessageId);
            if (metadata == null)
            {
                Debug.LogError($"[ClientNetworkEntry] 未知 MessageId={envelope.MessageId}，协议未在 MessageRegistry 中注册，已丢弃。");
                return;
            }

            if (metadata.Direction == MessageDirection.C2S)
            {
                Debug.LogError(
                    $"[ClientNetworkEntry] 非法协议方向：客户端入站链收到 C2S 协议，MessageId={envelope.MessageId}，Type={metadata.MessageType?.Name}，已阻断。");
                return;
            }

            // 状态机前置过滤拦截
            if (!_protocolFilter.CanReceive(metadata))
            {
                // 过滤器内部已打印警告或错误日志，此处直接丢弃
                return;
            }

            object message = _serializer.Deserialize(envelope.Payload, metadata.MessageType);
            if (message == null)
            {
                Debug.LogError(
                    $"[ClientNetworkEntry] 协议反序列化失败，MessageId={envelope.MessageId}，Type={metadata.MessageType?.Name}，已丢弃。");
                return;
            }

            if (metadata.Domain == MessageDomain.Global)
            {
                _globalRouter.Dispatch(metadata, message);
                return;
            }

            if (metadata.Domain == MessageDomain.Room)
            {
                // 房间域消息必须路由到当前在线房间实例
                var currentRoom = _globalClientManager.CurrentRoom;

                if (currentRoom == null)
                {
                    Debug.LogWarning(
                        $"[ClientNetworkEntry] 收到房间域消息但当前无在线房间实例，MessageId={envelope.MessageId}，Type={metadata.MessageType?.Name}，已丢弃。");
                    return;
                }

                if (string.IsNullOrEmpty(_sessionContext.CurrentRoomId))
                {
                    Debug.LogError(
                        $"[ClientNetworkEntry] 收到房间域消息但 SessionContext 中 RoomId 为空，MessageId={envelope.MessageId}，已丢弃。");
                    return;
                }

                if (envelope.RoomId != _sessionContext.CurrentRoomId)
                {
                    Debug.LogError(
                        $"[ClientNetworkEntry] 房间域消息 RoomId 不一致，Envelope.RoomId={envelope.RoomId}，Client.CurrentRoomId={_sessionContext.CurrentRoomId}，MessageId={envelope.MessageId}，已丢弃。");
                    return;
                }

                if (envelope.RoomId != currentRoom.RoomId)
                {
                    Debug.LogError(
                        $"[ClientNetworkEntry] 房间域消息 RoomId 与当前实例不一致，Envelope={envelope.RoomId}，Instance={currentRoom.RoomId}，已丢弃。");
                    return;
                }

                // 转发给当前房间实例的 Router
                currentRoom.MessageRouter.Dispatch(metadata, message, envelope.RoomId);
            }
        }
    }
}