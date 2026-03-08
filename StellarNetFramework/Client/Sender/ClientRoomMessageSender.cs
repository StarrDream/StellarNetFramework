using StellarNet.Client.Adapter;
using StellarNet.Client.Session;
using StellarNet.Shared.Envelope;
using StellarNet.Shared.Protocol;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Client.Sender
{
    /// <summary>
    /// 客户端房间域发送器，只允许发送 C2SRoomMessage 类型协议。
    /// 发送时自动从 ClientSessionContext 读取 CurrentRoomId 写入 Envelope.RoomId。
    /// 不在房间中时禁止发送房间域协议，直接报错阻断。
    /// </summary>
    public sealed class ClientRoomMessageSender
    {
        private readonly MirrorClientAdapter _adapter;
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly ClientSessionContext _sessionContext;

        /// <summary>
        /// 当前发送器是否处于可用状态。
        /// </summary>
        public bool IsAvailable =>
            _adapter != null &&
            _messageRegistry != null &&
            _serializer != null &&
            _sessionContext != null;

        public ClientRoomMessageSender(
            MirrorClientAdapter adapter,
            MessageRegistry messageRegistry,
            ISerializer serializer,
            ClientSessionContext sessionContext)
        {
            if (adapter == null)
            {
                Debug.LogError("[ClientRoomMessageSender] 构造失败：adapter 为 null。");
                return;
            }

            if (messageRegistry == null)
            {
                Debug.LogError("[ClientRoomMessageSender] 构造失败：messageRegistry 为 null。");
                return;
            }

            if (serializer == null)
            {
                Debug.LogError("[ClientRoomMessageSender] 构造失败：serializer 为 null。");
                return;
            }

            if (sessionContext == null)
            {
                Debug.LogError("[ClientRoomMessageSender] 构造失败：sessionContext 为 null。");
                return;
            }

            _adapter = adapter;
            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _sessionContext = sessionContext;
        }

        public void Send<TMessage>(TMessage message)
            where TMessage : C2SRoomMessage
        {
            if (!IsAvailable)
            {
                Debug.LogError($"[ClientRoomMessageSender] Send 失败：发送器未完成有效初始化，消息类型={typeof(TMessage).Name}。");
                return;
            }

            if (message == null)
            {
                Debug.LogError($"[ClientRoomMessageSender] Send 失败：message 为 null，消息类型={typeof(TMessage).Name}。");
                return;
            }

            if (!_sessionContext.IsInRoom)
            {
                Debug.LogError(
                    $"[ClientRoomMessageSender] Send 失败：当前不在任何房间中，不允许发送房间域协议 {typeof(TMessage).Name}，当前 CurrentRoomId={_sessionContext.CurrentRoomId}。");
                return;
            }

            if (!_sessionContext.IsLoggedIn)
            {
                Debug.LogError($"[ClientRoomMessageSender] Send 失败：当前未登录，不允许发送房间域协议 {typeof(TMessage).Name}。");
                return;
            }

            var metadata = _messageRegistry.GetByType<TMessage>();
            if (metadata == null)
            {
                Debug.LogError(
                    $"[ClientRoomMessageSender] Send 失败：消息类型 {typeof(TMessage).Name} 未在 MessageRegistry 中注册。");
                return;
            }

            byte[] payload = _serializer.Serialize(message);
            if (payload == null)
            {
                Debug.LogError(
                    $"[ClientRoomMessageSender] Send 失败：消息序列化结果为 null，MessageId={metadata.MessageId}，RoomId={_sessionContext.CurrentRoomId}。");
                return;
            }

            var envelope = new NetworkEnvelope(metadata.MessageId, payload, _sessionContext.CurrentRoomId);
            _adapter.Send(envelope);
        }
    }
}