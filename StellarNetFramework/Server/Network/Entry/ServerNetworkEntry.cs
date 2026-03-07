// Assets/StellarNetFramework/Server/Network/Entry/ServerNetworkEntry.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.Envelope;
using StellarNet.Shared.Protocol.Base;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using StellarNet.Server.Network.Adapter;
using StellarNet.Server.Network.Router;
using StellarNet.Server.Session;

namespace StellarNet.Server.Network.Entry
{
    // 服务端统一网络入口，是所有客户端上行消息进入框架的唯一门户。
    // 职责严格限定为：接收 Adapter 上抛的 NetworkEnvelope、通过 MessageRegistry 查找协议类型、
    // 完成反序列化、判断协议域归属、执行房间归属一致性校验链、转发给对应路由器。
    // 明确禁止：在入口层编写任何业务逻辑、直接修改业务状态、直接抛领域事件。
    // 房间归属一致性校验链位于域分流之后，只对 C2SRoomMessage 执行，
    // C2SGlobalMessage 不得误入房间归属一致性校验链。
    public sealed class ServerNetworkEntry
    {
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly SessionManager _sessionManager;
        private readonly ServerGlobalMessageRouter _globalRouter;

        // 房间域路由委托：由 GlobalRoomManager 在初始化后注入，
        // 避免 ServerNetworkEntry 直接持有 GlobalRoomManager 引用产生循环依赖。
        // 参数1：通过校验链后的 ConnectionId
        // 参数2：反序列化后的 C2SRoomMessage 实例
        // 参数3：当前消息所属 RoomId（来自 NetworkEnvelope 运行时上下文）
        private Func<ConnectionId, C2SRoomMessage, string, bool> _roomDomainRouter;

        public ServerNetworkEntry(
            MessageRegistry messageRegistry,
            ISerializer serializer,
            SessionManager sessionManager,
            ServerGlobalMessageRouter globalRouter)
        {
            if (messageRegistry == null)
            {
                Debug.LogError("[ServerNetworkEntry] 初始化失败：messageRegistry 不得为 null");
                return;
            }

            if (serializer == null)
            {
                Debug.LogError("[ServerNetworkEntry] 初始化失败：serializer 不得为 null");
                return;
            }

            if (sessionManager == null)
            {
                Debug.LogError("[ServerNetworkEntry] 初始化失败：sessionManager 不得为 null");
                return;
            }

            if (globalRouter == null)
            {
                Debug.LogError("[ServerNetworkEntry] 初始化失败：globalRouter 不得为 null");
                return;
            }

            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _sessionManager = sessionManager;
            _globalRouter = globalRouter;
        }

        // 注入房间域路由委托，由 GlobalRoomManager 初始化完成后调用
        public void SetRoomDomainRouter(Func<ConnectionId, C2SRoomMessage, string, bool> roomDomainRouter)
        {
            if (roomDomainRouter == null)
            {
                Debug.LogError("[ServerNetworkEntry] SetRoomDomainRouter 失败：委托不得为 null");
                return;
            }

            _roomDomainRouter = roomDomainRouter;
        }

        // 绑定到 INetworkAdapter 的 OnDataReceived 事件，由 GlobalInfrastructure 在装配阶段完成订阅
        public void OnAdapterDataReceived(ConnectionId connectionId, NetworkEnvelope envelope)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 收到无效 ConnectionId 的数据包，已丢弃。" +
                    $"ConnectionId={connectionId}，MessageId={envelope?.MessageId}");
                return;
            }

            if (envelope == null)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 收到 null NetworkEnvelope，已丢弃。" +
                    $"ConnectionId={connectionId}");
                return;
            }

            // 步骤一：通过 MessageId 查询注册元数据
            var meta = _messageRegistry.GetMetaById(envelope.MessageId);
            if (meta == null)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 未知 MessageId={envelope.MessageId}，" +
                    $"ConnectionId={connectionId}，数据包已丢弃。" +
                    $"请检查协议是否已正确注册到本端 MessageRegistry。");
                return;
            }

            // 步骤二：拦截服务端入站链收到 S2C 协议的非法情况
            if (meta.Direction == Shared.Enums.MessageDirection.S2C)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 非法协议阻断：收到 S2C 方向协议 {meta.MessageType.Name}，" +
                    $"MessageId={envelope.MessageId}，ConnectionId={connectionId}，" +
                    $"服务端入站链不允许接收 S2C 协议，数据包已丢弃。");
                return;
            }

            // 步骤三：反序列化协议体
            object messageObj = null;
            messageObj = _serializer.Deserialize(envelope.Payload, meta.MessageType);

            if (messageObj == null)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 反序列化失败：MessageId={envelope.MessageId}，" +
                    $"Type={meta.MessageType.Name}，ConnectionId={connectionId}，数据包已丢弃。");
                return;
            }

            // 步骤四：按域归属分流
            if (meta.Domain == Shared.Enums.MessageDomain.Global)
            {
                // 全局域：直接转发给 ServerGlobalMessageRouter，不进入房间归属一致性校验链
                var globalMsg = messageObj as C2SGlobalMessage;
                if (globalMsg == null)
                {
                    Debug.LogError(
                        $"[ServerNetworkEntry] 全局域协议类型转换失败：" +
                        $"MessageId={envelope.MessageId}，Type={meta.MessageType.Name}，" +
                        $"ConnectionId={connectionId}，数据包已丢弃。");
                    return;
                }

                _globalRouter.Dispatch(connectionId, globalMsg);
                return;
            }

            // 步骤五：房间域 — 执行房间归属一致性校验链
            var roomMsg = messageObj as C2SRoomMessage;
            if (roomMsg == null)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 房间域协议类型转换失败：" +
                    $"MessageId={envelope.MessageId}，Type={meta.MessageType.Name}，" +
                    $"ConnectionId={connectionId}，数据包已丢弃。");
                return;
            }

            // 校验一：ConnectionId 是否有效（已在入口处校验，此处为防御性二次确认）
            if (!connectionId.IsValid)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 房间归属校验失败：ConnectionId 无效，" +
                    $"MessageId={envelope.MessageId}，ConnectionId={connectionId}，消息已丢弃。");
                return;
            }

            // 校验二：ConnectionId 是否已绑定有效会话
            var session = _sessionManager.GetSessionByConnection(connectionId);
            if (session == null)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 房间归属校验失败：ConnectionId={connectionId} 未绑定有效会话，" +
                    $"MessageId={envelope.MessageId}，消息已丢弃。");
                return;
            }

            // 校验三：会话是否存在 CurrentRoomId
            if (!session.IsInRoom)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 房间归属校验失败：SessionId={session.SessionId} 当前不在任何房间内，" +
                    $"MessageId={envelope.MessageId}，ConnectionId={connectionId}，消息已丢弃。");
                return;
            }

            // 校验四：运行时 RoomId 是否与 CurrentRoomId 完全一致
            if (!string.Equals(envelope.RoomId, session.CurrentRoomId, StringComparison.Ordinal))
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 房间归属校验失败：RoomId 不一致，" +
                    $"EnvelopeRoomId={envelope.RoomId}，SessionCurrentRoomId={session.CurrentRoomId}，" +
                    $"SessionId={session.SessionId}，ConnectionId={connectionId}，" +
                    $"MessageId={envelope.MessageId}，消息已丢弃。");
                return;
            }

            // 步骤六：校验通过，转发给房间域路由委托
            if (_roomDomainRouter == null)
            {
                Debug.LogError(
                    $"[ServerNetworkEntry] 房间域路由委托未注入，" +
                    $"MessageId={envelope.MessageId}，ConnectionId={connectionId}，" +
                    $"RoomId={envelope.RoomId}，消息已丢弃。请确认 GlobalRoomManager 已完成初始化并注入路由委托。");
                return;
            }

            _roomDomainRouter.Invoke(connectionId, roomMsg, envelope.RoomId);
        }
    }
}
