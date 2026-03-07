// Assets/StellarNetFramework/Client/Network/Sender/ClientMessageSender.cs

using UnityEngine;
using StellarNet.Shared.Protocol.Base;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using StellarNet.Shared.Protocol.Envelope;
using StellarNet.Client.Network.Adapter;
using StellarNet.Client.Session;

namespace StellarNet.Client.Network.Sender
{
    // 客户端统一消息发送器，同时支持全局域与房间域消息的发送。
    // 客户端发送器不区分 GlobalSender 与 RoomSender，原因是客户端只有单连接单上下文，
    // 不存在服务端多房间并发发送的场景，合并为单一发送器降低客户端调用复杂度。
    // 发送房间域消息时，RoomId 由 ClientSessionContext 自动填充，不允许调用方手动指定，
    // 防止客户端伪造 RoomId 上传（服务端会在 ServerNetworkEntry 再次校验）。
    public sealed class ClientMessageSender
    {
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly IClientNetworkAdapter _adapter;
        private readonly ClientSessionContext _sessionContext;

        public ClientMessageSender(
            MessageRegistry messageRegistry,
            ISerializer serializer,
            IClientNetworkAdapter adapter,
            ClientSessionContext sessionContext)
        {
            if (messageRegistry == null)
            {
                Debug.LogError("[ClientMessageSender] 初始化失败：messageRegistry 不得为 null");
                return;
            }

            if (serializer == null)
            {
                Debug.LogError("[ClientMessageSender] 初始化失败：serializer 不得为 null");
                return;
            }

            if (adapter == null)
            {
                Debug.LogError("[ClientMessageSender] 初始化失败：adapter 不得为 null");
                return;
            }

            if (sessionContext == null)
            {
                Debug.LogError("[ClientMessageSender] 初始化失败：sessionContext 不得为 null");
                return;
            }

            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _adapter = adapter;
            _sessionContext = sessionContext;
        }

        // 发送全局域上行消息（C2SGlobalMessage）
        // RoomId 字段留空，全局域消息不携带房间上下文
        public void SendGlobal(C2SGlobalMessage message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientMessageSender] SendGlobal 失败：message 不得为 null");
                return;
            }

            if (!_adapter.IsConnected)
            {
                Debug.LogError(
                    $"[ClientMessageSender] SendGlobal 失败：当前未连接到服务端，" +
                    $"MessageType={message.GetType().Name}");
                return;
            }

            var envelope = BuildEnvelope(message, roomId: string.Empty);
            if (envelope == null)
                return;

            var meta = _messageRegistry.GetMetaByType(message.GetType());
            _adapter.Send(envelope, meta.DeliveryMode);
        }

        // 发送房间域上行消息（C2SRoomMessage）
        // RoomId 由 ClientSessionContext.CurrentRoomId 自动填充，不允许调用方手动指定
        public void SendRoom(C2SRoomMessage message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientMessageSender] SendRoom 失败：message 不得为 null");
                return;
            }

            if (!_adapter.IsConnected)
            {
                Debug.LogError(
                    $"[ClientMessageSender] SendRoom 失败：当前未连接到服务端，" +
                    $"MessageType={message.GetType().Name}");
                return;
            }

            if (!_sessionContext.IsInRoom)
            {
                Debug.LogError(
                    $"[ClientMessageSender] SendRoom 失败：当前不在任何房间内，" +
                    $"MessageType={message.GetType().Name}，" +
                    $"请确认客户端已成功加入房间后再发送房间域消息。");
                return;
            }

            // RoomId 由 SessionContext 自动填充，防止客户端伪造
            var envelope = BuildEnvelope(message, roomId: _sessionContext.CurrentRoomId);
            if (envelope == null)
                return;

            var meta = _messageRegistry.GetMetaByType(message.GetType());
            _adapter.Send(envelope, meta.DeliveryMode);
        }

        // 构建 NetworkEnvelope，完成序列化与 MessageId 解析
        private NetworkEnvelope BuildEnvelope(object message, string roomId)
        {
            var meta = _messageRegistry.GetMetaByType(message.GetType());
            if (meta == null)
            {
                Debug.LogError(
                    $"[ClientMessageSender] BuildEnvelope 失败：" +
                    $"协议类型 {message.GetType().Name} 未在 MessageRegistry 中注册，" +
                    $"请检查本端程序集白名单配置。");
                return null;
            }

            var payload = _serializer.Serialize(message);
            if (payload == null)
            {
                Debug.LogError(
                    $"[ClientMessageSender] BuildEnvelope 失败：序列化结果为 null，" +
                    $"MessageType={message.GetType().Name}，MessageId={meta.MessageId}");
                return null;
            }

            return new NetworkEnvelope(meta.MessageId, payload, roomId);
        }
    }
}
