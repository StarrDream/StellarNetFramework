// Assets/StellarNetFramework/Server/Network/Router/ServerRoomMessageRouter.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.Base;

namespace StellarNet.Server.Network.Router
{
    // 服务端房间域消息路由器，每个 RoomInstance 内部持有自己的独立实例。
    // 不是服务端全局单例房间路由器。
    // 职责严格限定为：管理房间域协议到 Handler 的注册表与分发。
    // Handler 必须随房间业务组件装配过程动态注册，由 ServerRoomAssembler 发起，
    // 房间业务组件自身不得在 Init()/OnDestroy() 中直接调用 Router 完成注册/注销。
    // 同一 RoomInstance 内，同一协议类型只能存在一个主处理 Handler。
    // 注册冲突时直接报错并阻断当前房间装配流程。
    // 动态注销时必须校验注销来源与原注册来源一致，防止误删其他组件绑定。
    public sealed class ServerRoomMessageRouter
    {
        // 所属房间 ID，用于错误日志定位
        private readonly string _roomId;

        // 协议 Type → Handler 记录（含来源组件标识，用于注销校验）
        private readonly Dictionary<Type, HandlerRecord> _handlers
            = new Dictionary<Type, HandlerRecord>();

        public ServerRoomMessageRouter(string roomId)
        {
            _roomId = roomId ?? string.Empty;
        }

        // 注册房间域协议 Handler。
        // 参数 messageType：协议运行时类型，必须继承自 C2SRoomMessage。
        // 参数 handler：主处理委托，不得为 null。
        // 参数 ownerComponentId：注册来源组件的稳定注册标识，用于注销时校验来源一致性。
        // 重复注册同一类型时直接报错阻断，由调用方（ServerRoomAssembler）决定是否回滚装配。
        public void Register(
            Type messageType,
            Action<ConnectionId, C2SRoomMessage> handler,
            string ownerComponentId)
        {
            if (messageType == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} Register 失败：messageType 不得为 null，" +
                    $"OwnerComponentId={ownerComponentId}");
                return;
            }

            if (handler == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} Register 失败：handler 不得为 null，" +
                    $"Type={messageType.Name}，OwnerComponentId={ownerComponentId}");
                return;
            }

            if (string.IsNullOrEmpty(ownerComponentId))
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} Register 失败：ownerComponentId 不得为空，" +
                    $"Type={messageType.Name}");
                return;
            }

            if (!typeof(C2SRoomMessage).IsAssignableFrom(messageType))
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} Register 失败：" +
                    $"类型 {messageType.Name} 未继承 C2SRoomMessage，" +
                    $"房间域路由器只接受 C2SRoomMessage 子类型，OwnerComponentId={ownerComponentId}");
                return;
            }

            if (_handlers.ContainsKey(messageType))
            {
                var existing = _handlers[messageType];
                Debug.LogError(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} Register 冲突：" +
                    $"协议类型 {messageType.Name} 已由组件 {existing.OwnerComponentId} 注册主处理 Handler，" +
                    $"当前组件 {ownerComponentId} 尝试重复注册，已阻断。" +
                    $"同一 RoomInstance 内同一协议类型只允许一个主处理者。");
                return;
            }

            _handlers[messageType] = new HandlerRecord(handler, ownerComponentId);
        }

        // 泛型注册重载
        public void Register<TMessage>(
            Action<ConnectionId, TMessage> handler,
            string ownerComponentId)
            where TMessage : C2SRoomMessage
        {
            if (handler == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} Register 失败：handler 不得为 null，" +
                    $"Type={typeof(TMessage).Name}，OwnerComponentId={ownerComponentId}");
                return;
            }

            Register(
                typeof(TMessage),
                (connId, msg) => handler(connId, (TMessage)msg),
                ownerComponentId);
        }

        // 注销房间域协议 Handler。
        // 参数 ownerComponentId：必须与原注册来源一致，防止误删其他组件绑定。
        // 来源不一致时直接报错阻断，不执行注销。
        public void Unregister(Type messageType, string ownerComponentId)
        {
            if (messageType == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} Unregister 失败：messageType 不得为 null，" +
                    $"OwnerComponentId={ownerComponentId}");
                return;
            }

            if (string.IsNullOrEmpty(ownerComponentId))
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} Unregister 失败：ownerComponentId 不得为空，" +
                    $"Type={messageType.Name}");
                return;
            }

            if (!_handlers.TryGetValue(messageType, out var record))
            {
                Debug.LogWarning(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} Unregister 警告：" +
                    $"协议类型 {messageType.Name} 未注册，本次注销已忽略，" +
                    $"OwnerComponentId={ownerComponentId}");
                return;
            }

            // 来源一致性校验：防止误删其他组件绑定
            if (!string.Equals(record.OwnerComponentId, ownerComponentId, StringComparison.Ordinal))
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} Unregister 阻断：" +
                    $"协议类型 {messageType.Name} 的注册来源为 {record.OwnerComponentId}，" +
                    $"当前注销来源为 {ownerComponentId}，来源不一致，注销已阻断，防止误删其他组件绑定。");
                return;
            }

            _handlers.Remove(messageType);
        }

        // 泛型注销重载
        public void Unregister<TMessage>(string ownerComponentId) where TMessage : C2SRoomMessage
        {
            Unregister(typeof(TMessage), ownerComponentId);
        }

        // 分发房间域消息到对应主处理 Handler。
        // 此时消息已通过房间归属一致性校验链，无需在此重复校验。
        public void Dispatch(ConnectionId connectionId, C2SRoomMessage message)
        {
            if (message == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} Dispatch 失败：message 不得为 null，" +
                    $"ConnectionId={connectionId}");
                return;
            }

            var messageType = message.GetType();

            if (!_handlers.TryGetValue(messageType, out var record))
            {
                Debug.LogWarning(
                    $"[ServerRoomMessageRouter] RoomId={_roomId} 未找到协议类型 {messageType.Name} 的主处理 Handler，" +
                    $"ConnectionId={connectionId}，消息已丢弃。" +
                    $"请确认对应房间业务组件已完成装配并注册 Handler。");
                return;
            }

            record.Handler.Invoke(connectionId, message);
        }

        // 清空全部 Handler 注册，由 RoomInstance 销毁流程调用
        public void Clear()
        {
            _handlers.Clear();
        }

        // 当前已注册 Handler 数量，用于诊断
        public int RegisteredHandlerCount => _handlers.Count;

        // Handler 注册记录，携带来源组件标识用于注销校验
        private sealed class HandlerRecord
        {
            public Action<ConnectionId, C2SRoomMessage> Handler { get; }
            public string OwnerComponentId { get; }

            public HandlerRecord(Action<ConnectionId, C2SRoomMessage> handler, string ownerComponentId)
            {
                Handler = handler;
                OwnerComponentId = ownerComponentId;
            }
        }
    }
}
