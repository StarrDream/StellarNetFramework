using System.Collections.Generic;
using StellarNet.Server.Session;
using StellarNet.Shared.Protocol;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Server.Sender
{
    /// <summary>
    /// 服务端全局域发送器，只允许发送 S2CGlobalMessage 类型协议。
    /// 对外方法显式体现投递范围：单播、全局广播、带目标房间参数的全局消息发送。
    /// 全局域消息默认不依赖房间上下文，当语义需要目标房间参数时通过显式重载传入 targetRoomId。
    /// targetRoomId 仅表示该次发送语义中的目标房间参数，不将该消息提升为房间域消息。
    /// 发送器不负责目标连接集解析，所有投递范围判定由 ServerSendCoordinator 完成。
    /// </summary>
    public sealed class ServerGlobalMessageSender
    {
        private readonly ServerSendCoordinator _coordinator;
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly SessionManager _sessionManager;

        public ServerGlobalMessageSender(
            ServerSendCoordinator coordinator,
            MessageRegistry messageRegistry,
            ISerializer serializer,
            SessionManager sessionManager)
        {
            if (coordinator == null)
            {
                Debug.LogError("[ServerGlobalMessageSender] 构造失败：coordinator 为 null。");
                return;
            }
            if (messageRegistry == null)
            {
                Debug.LogError("[ServerGlobalMessageSender] 构造失败：messageRegistry 为 null。");
                return;
            }
            if (serializer == null)
            {
                Debug.LogError("[ServerGlobalMessageSender] 构造失败：serializer 为 null。");
                return;
            }
            if (sessionManager == null)
            {
                Debug.LogError("[ServerGlobalMessageSender] 构造失败：sessionManager 为 null。");
                return;
            }

            _coordinator = coordinator;
            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _sessionManager = sessionManager;
        }

        /// <summary>
        /// 向指定 SessionId 的客户端单播全局域消息。
        /// 目标 Session 不在线时输出 Warning 并跳过，不视为错误。
        /// </summary>
        public void SendToSession<TMessage>(string sessionId, TMessage message)
            where TMessage : S2CGlobalMessage
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError($"[ServerGlobalMessageSender] SendToSession 失败：sessionId 为空，消息类型={typeof(TMessage).Name}。");
                return;
            }

            if (message == null)
            {
                Debug.LogError($"[ServerGlobalMessageSender] SendToSession 失败：message 为 null，SessionId={sessionId}，消息类型={typeof(TMessage).Name}。");
                return;
            }

            var metadata = _messageRegistry.GetByType<TMessage>();
            if (metadata == null)
            {
                Debug.LogError($"[ServerGlobalMessageSender] SendToSession 失败：消息类型 {typeof(TMessage).Name} 未在 MessageRegistry 中注册，SessionId={sessionId}。");
                return;
            }

            byte[] payload = _serializer.Serialize(message);
            if (payload == null)
            {
                Debug.LogError($"[ServerGlobalMessageSender] SendToSession 失败：消息序列化结果为 null，SessionId={sessionId}，MessageId={metadata.MessageId}。");
                return;
            }

            var session = _sessionManager.GetSessionById(sessionId);
            if (session == null)
            {
                Debug.LogWarning($"[ServerGlobalMessageSender] SendToSession 跳过：SessionId={sessionId} 不存在，MessageId={metadata.MessageId}。");
                return;
            }

            if (!session.IsOnline)
            {
                Debug.LogWarning($"[ServerGlobalMessageSender] SendToSession 跳过：SessionId={sessionId} 当前不在线，MessageId={metadata.MessageId}。");
                return;
            }

            // 全局域消息 NetworkEnvelope.RoomId 为空字符串
            _coordinator.DispatchToConnection(session.CurrentConnectionId, metadata.MessageId, payload, roomId: string.Empty);
        }

        /// <summary>
        /// 向指定 SessionId 的客户端单播全局域消息，同时附带目标房间参数。
        /// targetRoomId 仅表示该次发送语义中的目标房间参数，不将该消息提升为房间域消息。
        /// 是否校验 targetRoomId 指向的房间真实存在，由具体发送场景与上层业务逻辑决定。
        /// </summary>
        public void SendToSessionWithTargetRoom<TMessage>(string sessionId, TMessage message, string targetRoomId)
            where TMessage : S2CGlobalMessage
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError($"[ServerGlobalMessageSender] SendToSessionWithTargetRoom 失败：sessionId 为空，消息类型={typeof(TMessage).Name}。");
                return;
            }

            if (message == null)
            {
                Debug.LogError($"[ServerGlobalMessageSender] SendToSessionWithTargetRoom 失败：message 为 null，SessionId={sessionId}，消息类型={typeof(TMessage).Name}。");
                return;
            }

            var metadata = _messageRegistry.GetByType<TMessage>();
            if (metadata == null)
            {
                Debug.LogError($"[ServerGlobalMessageSender] SendToSessionWithTargetRoom 失败：消息类型 {typeof(TMessage).Name} 未在 MessageRegistry 中注册，SessionId={sessionId}。");
                return;
            }

            byte[] payload = _serializer.Serialize(message);
            if (payload == null)
            {
                Debug.LogError($"[ServerGlobalMessageSender] SendToSessionWithTargetRoom 失败：消息序列化结果为 null，SessionId={sessionId}，MessageId={metadata.MessageId}。");
                return;
            }

            var session = _sessionManager.GetSessionById(sessionId);
            if (session == null)
            {
                Debug.LogWarning($"[ServerGlobalMessageSender] SendToSessionWithTargetRoom 跳过：SessionId={sessionId} 不存在，MessageId={metadata.MessageId}。");
                return;
            }

            if (!session.IsOnline)
            {
                Debug.LogWarning($"[ServerGlobalMessageSender] SendToSessionWithTargetRoom 跳过：SessionId={sessionId} 当前不在线，MessageId={metadata.MessageId}。");
                return;
            }

            // targetRoomId 写入 Envelope.RoomId 作为目标房间参数透传，不等价于房间域消息的归属上下文
            _coordinator.DispatchToConnection(session.CurrentConnectionId, metadata.MessageId, payload, roomId: targetRoomId ?? string.Empty);
        }

        /// <summary>
        /// 向所有在线客户端广播全局域消息。
        /// 广播目标集合为空时不视为一次有效广播，输出 Warning 并跳过。
        /// </summary>
        public void BroadcastToAll<TMessage>(TMessage message)
            where TMessage : S2CGlobalMessage
        {
            if (message == null)
            {
                Debug.LogError($"[ServerGlobalMessageSender] BroadcastToAll 失败：message 为 null，消息类型={typeof(TMessage).Name}。");
                return;
            }

            var metadata = _messageRegistry.GetByType<TMessage>();
            if (metadata == null)
            {
                Debug.LogError($"[ServerGlobalMessageSender] BroadcastToAll 失败：消息类型 {typeof(TMessage).Name} 未在 MessageRegistry 中注册。");
                return;
            }

            byte[] payload = _serializer.Serialize(message);
            if (payload == null)
            {
                Debug.LogError($"[ServerGlobalMessageSender] BroadcastToAll 失败：消息序列化结果为 null，MessageId={metadata.MessageId}。");
                return;
            }

            // 收集所有在线连接目标集合，由发送器完成，Coordinator 只负责逐连接投递
            var targetConnections = _sessionManager.GetAllOnlineConnectionIds();
            if (targetConnections == null || targetConnections.Count == 0)
            {
                Debug.LogWarning($"[ServerGlobalMessageSender] BroadcastToAll 跳过：当前无在线客户端，MessageId={metadata.MessageId}。");
                return;
            }

            _coordinator.DispatchToConnections(targetConnections, metadata.MessageId, payload, roomId: string.Empty);
        }
    }
}
