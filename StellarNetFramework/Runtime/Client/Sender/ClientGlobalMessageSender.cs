using StellarNet.Client.Adapter;
using StellarNet.Client.Session;
using StellarNet.Shared.Envelope;
using StellarNet.Shared.Protocol;
using StellarNet.Shared.Protocol.BuiltIn;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Client.Sender
{
    /// <summary>
    /// 客户端全局域发送器，只允许发送 C2SGlobalMessage 类型协议。
    /// 发送时自动从 ClientSessionContext 读取 SessionId 写入 Envelope 上下文。
    /// 未登录时只允许发送 C2S_Login 与 C2S_Reconnect。
    /// </summary>
    public sealed class ClientGlobalMessageSender
    {
        private readonly MirrorClientAdapter _adapter;
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly ClientSessionContext _sessionContext;

        /// <summary>
        /// 当前发送器是否处于可用状态。
        /// 构造器若因依赖缺失提前结束，对象仍可能被外层持有，因此必须显式暴露可用性。
        /// </summary>
        public bool IsAvailable =>
            _adapter != null &&
            _messageRegistry != null &&
            _serializer != null &&
            _sessionContext != null;

        public ClientGlobalMessageSender(
            MirrorClientAdapter adapter,
            MessageRegistry messageRegistry,
            ISerializer serializer,
            ClientSessionContext sessionContext)
        {
            if (adapter == null)
            {
                Debug.LogError("[ClientGlobalMessageSender] 构造失败：adapter 为 null。");
                return;
            }

            if (messageRegistry == null)
            {
                Debug.LogError("[ClientGlobalMessageSender] 构造失败：messageRegistry 为 null。");
                return;
            }

            if (serializer == null)
            {
                Debug.LogError("[ClientGlobalMessageSender] 构造失败：serializer 为 null。");
                return;
            }

            if (sessionContext == null)
            {
                Debug.LogError("[ClientGlobalMessageSender] 构造失败：sessionContext 为 null。");
                return;
            }

            _adapter = adapter;
            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _sessionContext = sessionContext;
        }

        public void Send<TMessage>(TMessage message)
            where TMessage : C2SGlobalMessage
        {
            if (!IsAvailable)
            {
                Debug.LogError($"[ClientGlobalMessageSender] Send 失败：发送器未完成有效初始化，消息类型={typeof(TMessage).Name}。");
                return;
            }

            if (message == null)
            {
                Debug.LogError($"[ClientGlobalMessageSender] Send 失败：message 为 null，消息类型={typeof(TMessage).Name}。");
                return;
            }

            var metadata = _messageRegistry.GetByType<TMessage>();
            if (metadata == null)
            {
                Debug.LogError(
                    $"[ClientGlobalMessageSender] Send 失败：消息类型 {typeof(TMessage).Name} 未在 MessageRegistry 中注册。");
                return;
            }

            bool isLoginOrReconnect = message is C2S_Login || message is C2S_Reconnect;
            if (!_sessionContext.IsLoggedIn && !isLoginOrReconnect)
            {
                Debug.LogError($"[ClientGlobalMessageSender] Send 失败：当前未登录，不允许发送 {typeof(TMessage).Name}，请先完成登录流程。");
                return;
            }

            byte[] payload = _serializer.Serialize(message);
            if (payload == null)
            {
                Debug.LogError($"[ClientGlobalMessageSender] Send 失败：消息序列化结果为 null，MessageId={metadata.MessageId}。");
                return;
            }

            var envelope = new NetworkEnvelope(metadata.MessageId, payload, string.Empty);
            _adapter.Send(envelope);
        }
    }
}