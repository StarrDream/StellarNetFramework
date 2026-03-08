using StellarNet.Client.Adapter;
using StellarNet.Client.Session;
using StellarNet.Shared.Envelope;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Client.Network
{
    /// <summary>
    /// 客户端统一网络入口，是所有服务端下行消息进入框架的第一个处理节点。
    /// 职责严格限定为：接收 Adapter 上抛的 NetworkEnvelope、查表反序列化、方向校验、域分流、转发给对应路由器。
    /// 禁止在此层编写任何业务逻辑、修改业务状态或直接抛领域事件。
    /// 客户端入站链收到 C2S 协议时必须直接阻断。
    /// </summary>
    public sealed class ClientNetworkEntry
    {
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly ClientGlobalMessageRouter _globalRouter;
        private readonly ClientRoomMessageRouter _roomRouter;
        private readonly ClientSessionContext _sessionContext;

        /// <summary>
        /// 当前网络入口是否处于可用状态。
        /// 构造器内部若关键依赖为空，对象本身仍可能被外层持有，因此需要显式可用性标记。
        /// </summary>
        public bool IsAvailable =>
            _messageRegistry != null &&
            _serializer != null &&
            _globalRouter != null &&
            _roomRouter != null &&
            _sessionContext != null;

        public ClientNetworkEntry(
            MessageRegistry messageRegistry,
            ISerializer serializer,
            ClientGlobalMessageRouter globalRouter,
            ClientRoomMessageRouter roomRouter,
            ClientSessionContext sessionContext)
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

            if (roomRouter == null)
            {
                Debug.LogError("[ClientNetworkEntry] 构造失败：roomRouter 为 null。");
                return;
            }

            if (sessionContext == null)
            {
                Debug.LogError("[ClientNetworkEntry] 构造失败：sessionContext 为 null。");
                return;
            }

            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _globalRouter = globalRouter;
            _roomRouter = roomRouter;
            _sessionContext = sessionContext;
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
                if (string.IsNullOrEmpty(_sessionContext.CurrentRoomId))
                {
                    Debug.LogError(
                        $"[ClientNetworkEntry] 收到房间域消息但客户端当前不在任何房间中，MessageId={envelope.MessageId}，Envelope.RoomId={envelope.RoomId}，已丢弃。");
                    return;
                }

                if (envelope.RoomId != _sessionContext.CurrentRoomId)
                {
                    Debug.LogError(
                        $"[ClientNetworkEntry] 房间域消息 RoomId 不一致，Envelope.RoomId={envelope.RoomId}，Client.CurrentRoomId={_sessionContext.CurrentRoomId}，MessageId={envelope.MessageId}，已丢弃。");
                    return;
                }

                _roomRouter.Dispatch(metadata, message, envelope.RoomId);
            }
        }
    }
}