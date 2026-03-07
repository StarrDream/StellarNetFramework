// Assets/StellarNetFramework/Client/Network/Router/ClientRoomMessageRouter.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Protocol.Base;

namespace StellarNet.Client.Network.Router
{
    // 客户端房间域消息路由器，管理房间域 S2CRoomMessage 到 Handler 的注册表与分发。
    // 客户端只持有单个房间上下文，不需要按 RoomId 分桶，路由器为全局单例。
    // 收到房间域消息时，由 ClientNetworkEntry 校验 RoomId 与 ClientSessionContext 一致后再分发。
    // 同一协议类型只允许存在一个主处理 Handler，重复注册直接报错阻断。
    public sealed class ClientRoomMessageRouter
    {
        // 协议 Type → Handler 委托映射表
        private readonly Dictionary<Type, Action<S2CRoomMessage>> _handlers
            = new Dictionary<Type, Action<S2CRoomMessage>>();

        // 注册房间域 S2C 协议 Handler
        public void Register(Type messageType, Action<S2CRoomMessage> handler)
        {
            if (messageType == null)
            {
                Debug.LogError("[ClientRoomMessageRouter] Register 失败：messageType 不得为 null");
                return;
            }

            if (handler == null)
            {
                Debug.LogError(
                    $"[ClientRoomMessageRouter] Register 失败：handler 不得为 null，" +
                    $"Type={messageType.Name}");
                return;
            }

            if (!typeof(S2CRoomMessage).IsAssignableFrom(messageType))
            {
                Debug.LogError(
                    $"[ClientRoomMessageRouter] Register 失败：类型 {messageType.Name} " +
                    $"未继承 S2CRoomMessage，房间域路由器只接受 S2CRoomMessage 子类型。");
                return;
            }

            if (_handlers.ContainsKey(messageType))
            {
                Debug.LogError(
                    $"[ClientRoomMessageRouter] Register 冲突：协议类型 {messageType.Name} " +
                    $"已存在主处理 Handler，同一协议类型只允许一个主处理者，当前注册已阻断。");
                return;
            }

            _handlers[messageType] = handler;
        }

        // 泛型注册重载
        public void Register<TMessage>(Action<TMessage> handler)
            where TMessage : S2CRoomMessage
        {
            if (handler == null)
            {
                Debug.LogError(
                    $"[ClientRoomMessageRouter] Register 失败：handler 不得为 null，" +
                    $"Type={typeof(TMessage).Name}");
                return;
            }

            Register(typeof(TMessage), msg => handler((TMessage)msg));
        }

        // 注销房间域 S2C 协议 Handler
        public void Unregister(Type messageType)
        {
            if (messageType == null)
            {
                Debug.LogError("[ClientRoomMessageRouter] Unregister 失败：messageType 不得为 null");
                return;
            }

            if (!_handlers.ContainsKey(messageType))
            {
                Debug.LogWarning(
                    $"[ClientRoomMessageRouter] Unregister 警告：协议类型 {messageType.Name} " +
                    $"未注册，本次注销已忽略。");
                return;
            }

            _handlers.Remove(messageType);
        }

        // 泛型注销重载
        public void Unregister<TMessage>() where TMessage : S2CRoomMessage
        {
            Unregister(typeof(TMessage));
        }

        // 分发房间域 S2C 消息到对应主处理 Handler
        // 此时消息已通过 ClientNetworkEntry 的 RoomId 一致性校验
        public void Dispatch(S2CRoomMessage message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientRoomMessageRouter] Dispatch 失败：message 不得为 null");
                return;
            }

            var messageType = message.GetType();

            if (!_handlers.TryGetValue(messageType, out var handler))
            {
                Debug.LogWarning(
                    $"[ClientRoomMessageRouter] 未找到协议类型 {messageType.Name} 的主处理 Handler，" +
                    $"消息已丢弃。请确认对应客户端房间模块已完成 Handler 注册。");
                return;
            }

            handler.Invoke(message);
        }

        // 清空全部 Handler 注册，在离房时调用以防止旧房间消息误路由
        public void Clear()
        {
            _handlers.Clear();
        }

        // 当前已注册 Handler 数量，用于诊断
        public int RegisteredHandlerCount => _handlers.Count;
    }
}
