using System;
using System.Collections.Generic;
using StellarNet.Shared.Registry;
using UnityEngine;

namespace StellarNet.Client.Network
{
    /// <summary>
    /// 客户端房间域消息路由器，管理房间域协议到 Handle 的注册表并完成分发。
    /// 客户端同一时刻只在一个房间中，因此只需要一个全局 ClientRoomMessageRouter 实例。
    /// 房间业务组件通过 ClientRoomMessageRegistrar 向此路由器动态注册/注销处理委托。
    /// 进入房间时注册，离开房间时注销，保证路由表与当前房间状态严格同步。
    /// </summary>
    public sealed class ClientRoomMessageRouter
    {
        private readonly Dictionary<Type, Action<string, object>> _handlers
            = new Dictionary<Type, Action<string, object>>();

        /// <summary>
        /// 动态注册房间域协议处理委托。
        /// 同一协议类型只允许存在一个主处理委托，重复注册直接报错阻断。
        /// </summary>
        public bool Register(Type messageType, Action<string, object> handler)
        {
            if (messageType == null)
            {
                Debug.LogError("[ClientRoomMessageRouter] Register 失败：messageType 为 null。");
                return false;
            }
            if (handler == null)
            {
                Debug.LogError($"[ClientRoomMessageRouter] Register 失败：handler 为 null，messageType={messageType.Name}。");
                return false;
            }
            if (_handlers.ContainsKey(messageType))
            {
                Debug.LogError($"[ClientRoomMessageRouter] Register 失败：协议类型 {messageType.Name} 已存在主处理委托，禁止重复注册。");
                return false;
            }

            _handlers[messageType] = handler;
            return true;
        }

        /// <summary>
        /// 动态注销房间域协议处理委托。
        /// </summary>
        public void Unregister(Type messageType)
        {
            if (messageType == null)
            {
                return;
            }
            _handlers.Remove(messageType);
        }

        /// <summary>
        /// 分发房间域消息到对应的主处理委托。
        /// </summary>
        public void Dispatch(MessageMetadata metadata, object message, string roomId)
        {
            if (metadata == null)
            {
                Debug.LogError("[ClientRoomMessageRouter] Dispatch 失败：metadata 为 null。");
                return;
            }
            if (message == null)
            {
                Debug.LogError($"[ClientRoomMessageRouter] Dispatch 失败：message 为 null，MessageId={metadata.MessageId}，RoomId={roomId}。");
                return;
            }

            if (!_handlers.TryGetValue(metadata.MessageType, out var handler))
            {
                Debug.LogWarning($"[ClientRoomMessageRouter] 未找到协议 {metadata.MessageType?.Name}（MessageId={metadata.MessageId}）的处理者，RoomId={roomId}，消息已忽略。");
                return;
            }

            handler.Invoke(roomId, message);
        }

        /// <summary>
        /// 清空所有注册的处理委托，在离开房间时调用，确保不残留旧房间的处理委托。
        /// </summary>
        public void ClearAll()
        {
            _handlers.Clear();
        }
    }
}
