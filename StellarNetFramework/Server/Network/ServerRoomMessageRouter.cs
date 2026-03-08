using System;
using System.Collections.Generic;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Registry;
using UnityEngine;

namespace StellarNet.Server.Network
{
    /// <summary>
    /// 服务端房间域消息路由器，管理房间域协议到 Handle 的注册表并完成分发。
    /// 每个 RoomInstance 持有自己独立的 ServerRoomMessageRouter 实例，保证房间路由隔离。
    /// Router 实例在房间创建时即存在，但内部 Handle 注册表通过 ServerRoomAssembler 动态注册。
    /// 禁止预注册所有可能的房间业务 Handle，必须与当前实际已装配的组件集合严格一致。
    /// 注册/注销发起方必须是 ServerRoomAssembler，房间业务组件自身不得直接调用此 Router。
    /// </summary>
    public sealed class ServerRoomMessageRouter
    {
        private readonly Dictionary<Type, Action<ConnectionId, string, object>> _handlers
            = new Dictionary<Type, Action<ConnectionId, string, object>>();

        private readonly string _roomId;

        public ServerRoomMessageRouter(string roomId)
        {
            _roomId = roomId ?? string.Empty;
        }

        /// <summary>
        /// 动态注册房间域协议处理委托。
        /// 同一协议类型只允许存在一个主处理委托，重复注册直接报错并阻断当前装配流程。
        /// </summary>
        public bool Register(Type messageType, Action<ConnectionId, string, object> handler)
        {
            if (messageType == null)
            {
                Debug.LogError($"[ServerRoomMessageRouter] Register 失败：messageType 为 null，RoomId={_roomId}。");
                return false;
            }

            if (handler == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] Register 失败：handler 为 null，messageType={messageType.Name}，RoomId={_roomId}。");
                return false;
            }

            if (_handlers.ContainsKey(messageType))
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] Register 失败：协议类型 {messageType.Name} 在房间 {_roomId} 中已存在主处理委托，同一 RoomInstance 内不允许重复注册。");
                return false;
            }

            _handlers[messageType] = handler;
            return true;
        }

        /// <summary>
        /// 动态注销房间域协议处理委托。
        /// 注销时校验注销来源与原注册来源一致，防止误删其他组件绑定。
        /// </summary>
        public void Unregister(Type messageType, Action<ConnectionId, string, object> handler)
        {
            if (messageType == null)
            {
                Debug.LogError($"[ServerRoomMessageRouter] Unregister 失败：messageType 为 null，RoomId={_roomId}。");
                return;
            }

            if (!_handlers.TryGetValue(messageType, out var existingHandler))
            {
                Debug.LogWarning(
                    $"[ServerRoomMessageRouter] Unregister 警告：协议类型 {messageType.Name} 在房间 {_roomId} 中未找到注册记录。");
                return;
            }

            if (handler == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] Unregister 失败：handler 为 null，messageType={messageType.Name}，RoomId={_roomId}。");
                return;
            }

            if (existingHandler != handler)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] Unregister 失败：协议类型 {messageType.Name} 在房间 {_roomId} 中的注销来源与原注册来源不一致，已阻止误删。");
                return;
            }

            _handlers.Remove(messageType);
        }

        /// <summary>
        /// 按原始注册委托强制注销。
        /// 此方法服务于 RoomMessageRegistrar 的正常生命周期注销，不用于回滚。
        /// </summary>
        public void UnregisterByExactHandler(Type messageType, Action<ConnectionId, string, object> exactHandler)
        {
            if (messageType == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] UnregisterByExactHandler 失败：messageType 为 null，RoomId={_roomId}。");
                return;
            }

            if (exactHandler == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] UnregisterByExactHandler 失败：exactHandler 为 null，messageType={messageType.Name}，RoomId={_roomId}。");
                return;
            }

            if (!_handlers.TryGetValue(messageType, out var existingHandler))
            {
                Debug.LogWarning(
                    $"[ServerRoomMessageRouter] UnregisterByExactHandler 警告：协议类型 {messageType.Name} 在房间 {_roomId} 中未找到注册记录。");
                return;
            }

            if (existingHandler != exactHandler)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] UnregisterByExactHandler 失败：协议类型 {messageType.Name} 在房间 {_roomId} 中的注销来源与原注册来源不一致，已阻止误删。");
                return;
            }

            _handlers.Remove(messageType);
        }

        /// <summary>
        /// 分发房间域消息到对应的主处理委托。
        /// </summary>
        public void Dispatch(ConnectionId connectionId, string roomId, MessageMetadata metadata, object message)
        {
            if (metadata == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] Dispatch 失败：metadata 为 null，RoomId={_roomId}，ConnectionId={connectionId}。");
                return;
            }

            if (message == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] Dispatch 失败：message 为 null，MessageId={metadata.MessageId}，RoomId={_roomId}，ConnectionId={connectionId}。");
                return;
            }

            if (!_handlers.TryGetValue(metadata.MessageType, out var handler))
            {
                Debug.LogWarning(
                    $"[ServerRoomMessageRouter] 未找到协议 {metadata.MessageType?.Name}（MessageId={metadata.MessageId}）的处理者，RoomId={_roomId}，ConnectionId={connectionId}，消息已忽略。");
                return;
            }

            handler.Invoke(connectionId, roomId, message);
        }

        /// <summary>
        /// 强制按消息类型注销处理委托，专供装配失败回滚使用。
        /// </summary>
        public void ForceUnregisterByType(Type messageType)
        {
            if (messageType == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageRouter] ForceUnregisterByType 失败：messageType 为 null，RoomId={_roomId}。");
                return;
            }

            if (_handlers.ContainsKey(messageType))
            {
                _handlers.Remove(messageType);
                Debug.Log($"[ServerRoomMessageRouter] 强制注销协议类型 {messageType.Name}，RoomId={_roomId}（回滚专用）。");
            }
        }

        /// <summary>
        /// 清空所有注册的处理委托，由 ServerRoomAssembler 在房间销毁回滚或完整销毁流程中调用。
        /// </summary>
        public void ClearAll()
        {
            _handlers.Clear();
        }
    }
}