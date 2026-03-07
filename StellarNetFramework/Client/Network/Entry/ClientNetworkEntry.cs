// Assets/StellarNetFramework/Client/Network/Entry/ClientNetworkEntry.cs

using System;
using UnityEngine;
using StellarNet.Shared.Protocol.Envelope;
using StellarNet.Shared.Protocol.Base;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using StellarNet.Client.Network.Router;
using StellarNet.Client.Session;

namespace StellarNet.Client.Network.Entry
{
    // 客户端统一网络入口，是所有服务端下行消息进入客户端框架的唯一门户。
    // 职责严格限定为：接收 Adapter 上抛的 NetworkEnvelope、通过 MessageRegistry 查找协议类型、
    // 完成反序列化、判断协议域归属、对房间域消息执行 RoomId 一致性校验、转发给对应路由器。
    // 明确禁止：在入口层编写任何业务逻辑、直接修改业务状态、直接抛领域事件。
    // 客户端入口只处理 S2C 方向协议，收到 C2S 协议时直接报错阻断。
    public sealed class ClientNetworkEntry
    {
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly ClientSessionContext _sessionContext;
        private readonly ClientGlobalMessageRouter _globalRouter;
        private readonly ClientRoomMessageRouter _roomRouter;

        public ClientNetworkEntry(
            MessageRegistry messageRegistry,
            ISerializer serializer,
            ClientSessionContext sessionContext,
            ClientGlobalMessageRouter globalRouter,
            ClientRoomMessageRouter roomRouter)
        {
            if (messageRegistry == null)
            {
                Debug.LogError("[ClientNetworkEntry] 初始化失败：messageRegistry 不得为 null");
                return;
            }

            if (serializer == null)
            {
                Debug.LogError("[ClientNetworkEntry] 初始化失败：serializer 不得为 null");
                return;
            }

            if (sessionContext == null)
            {
                Debug.LogError("[ClientNetworkEntry] 初始化失败：sessionContext 不得为 null");
                return;
            }

            if (globalRouter == null)
            {
                Debug.LogError("[ClientNetworkEntry] 初始化失败：globalRouter 不得为 null");
                return;
            }

            if (roomRouter == null)
            {
                Debug.LogError("[ClientNetworkEntry] 初始化失败：roomRouter 不得为 null");
                return;
            }

            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _sessionContext = sessionContext;
            _globalRouter = globalRouter;
            _roomRouter = roomRouter;
        }

        // 绑定到 IClientNetworkAdapter 的 OnDataReceived 事件
        public void OnAdapterDataReceived(NetworkEnvelope envelope)
        {
            if (envelope == null)
            {
                Debug.LogError("[ClientNetworkEntry] 收到 null NetworkEnvelope，已丢弃。");
                return;
            }

            // 步骤一：通过 MessageId 查询注册元数据
            var meta = _messageRegistry.GetMetaById(envelope.MessageId);
            if (meta == null)
            {
                Debug.LogError(
                    $"[ClientNetworkEntry] 未知 MessageId={envelope.MessageId}，数据包已丢弃。" +
                    $"请检查协议是否已正确注册到本端 MessageRegistry。");
                return;
            }

            // 步骤二：拦截客户端入站链收到 C2S 协议的非法情况
            if (meta.Direction == Shared.Enums.MessageDirection.C2S)
            {
                Debug.LogError(
                    $"[ClientNetworkEntry] 非法协议阻断：收到 C2S 方向协议 {meta.MessageType.Name}，" +
                    $"MessageId={envelope.MessageId}，客户端入站链不允许接收 C2S 协议，数据包已丢弃。");
                return;
            }

            // 步骤三：反序列化协议体
            var messageObj = _serializer.Deserialize(envelope.Payload, meta.MessageType);
            if (messageObj == null)
            {
                Debug.LogError(
                    $"[ClientNetworkEntry] 反序列化失败：MessageId={envelope.MessageId}，" +
                    $"Type={meta.MessageType.Name}，数据包已丢弃。");
                return;
            }

            // 步骤四：按域归属分流
            if (meta.Domain == Shared.Enums.MessageDomain.Global)
            {
                var globalMsg = messageObj as S2CGlobalMessage;
                if (globalMsg == null)
                {
                    Debug.LogError(
                        $"[ClientNetworkEntry] 全局域协议类型转换失败：" +
                        $"MessageId={envelope.MessageId}，Type={meta.MessageType.Name}，数据包已丢弃。");
                    return;
                }

                _globalRouter.Dispatch(globalMsg);
                return;
            }

            // 步骤五：房间域 — 执行 RoomId 一致性校验
            var roomMsg = messageObj as S2CRoomMessage;
            if (roomMsg == null)
            {
                Debug.LogError(
                    $"[ClientNetworkEntry] 房间域协议类型转换失败：" +
                    $"MessageId={envelope.MessageId}，Type={meta.MessageType.Name}，数据包已丢弃。");
                return;
            }

            // 校验一：客户端当前是否处于房间内
            if (!_sessionContext.IsInRoom)
            {
                Debug.LogError(
                    $"[ClientNetworkEntry] 房间域消息校验失败：客户端当前不在任何房间内，" +
                    $"MessageId={envelope.MessageId}，Type={meta.MessageType.Name}，消息已丢弃。");
                return;
            }

            // 校验二：消息 RoomId 是否与当前会话 RoomId 一致
            if (!string.Equals(envelope.RoomId, _sessionContext.CurrentRoomId, StringComparison.Ordinal))
            {
                Debug.LogError(
                    $"[ClientNetworkEntry] 房间域消息 RoomId 不一致：" +
                    $"EnvelopeRoomId={envelope.RoomId}，" +
                    $"SessionCurrentRoomId={_sessionContext.CurrentRoomId}，" +
                    $"MessageId={envelope.MessageId}，消息已丢弃。");
                return;
            }

            // 步骤六：校验通过，转发给房间域路由器
            _roomRouter.Dispatch(roomMsg);
        }
    }
}
