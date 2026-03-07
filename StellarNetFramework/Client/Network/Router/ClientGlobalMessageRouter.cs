// Assets/StellarNetFramework/Client/Network/Router/ClientGlobalMessageRouter.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Protocol.Base;

namespace StellarNet.Client.Network.Router
{
    // 客户端全局域消息路由器，管理全局域 S2CGlobalMessage 到 Handler 的注册表与分发。
    // 同一协议类型只允许存在一个主处理 Handler，重复注册直接报错阻断。
    // Handler 注册由各客户端全局模块在 ClientInfrastructure 装配阶段完成。
    // 客户端路由器不需要 OwnerComponentId 校验，原因是客户端模块生命周期与服务端房间组件不同，
    // 客户端模块随 ClientInfrastructure 统一装配与销毁，不存在运行期动态注销冲突场景。
    public sealed class ClientGlobalMessageRouter
    {
        // 协议 Type → Handler 委托映射表
        private readonly Dictionary<Type, Action<S2CGlobalMessage>> _handlers
            = new Dictionary<Type, Action<S2CGlobalMessage>>();

        // 注册全局域 S2C 协议 Handler
        public void Register(Type messageType, Action<S2CGlobalMessage> handler)
        {
            if (messageType == null)
            {
                Debug.LogError("[ClientGlobalMessageRouter] Register 失败：messageType 不得为 null");
                return;
            }

            if (handler == null)
            {
                Debug.LogError(
                    $"[ClientGlobalMessageRouter] Register 失败：handler 不得为 null，" +
                    $"Type={messageType.Name}");
                return;
            }

            if (!typeof(S2CGlobalMessage).IsAssignableFrom(messageType))
            {
                Debug.LogError(
                    $"[ClientGlobalMessageRouter] Register 失败：类型 {messageType.Name} " +
                    $"未继承 S2CGlobalMessage，全局域路由器只接受 S2CGlobalMessage 子类型。");
                return;
            }

            if (_handlers.ContainsKey(messageType))
            {
                Debug.LogError(
                    $"[ClientGlobalMessageRouter] Register 冲突：协议类型 {messageType.Name} " +
                    $"已存在主处理 Handler，同一协议类型只允许一个主处理者，当前注册已阻断。");
                return;
            }

            _handlers[messageType] = handler;
        }

        // 泛型注册重载
        public void Register<TMessage>(Action<TMessage> handler)
            where TMessage : S2CGlobalMessage
        {
            if (handler == null)
            {
                Debug.LogError(
                    $"[ClientGlobalMessageRouter] Register 失败：handler 不得为 null，" +
                    $"Type={typeof(TMessage).Name}");
                return;
            }

            Register(typeof(TMessage), msg => handler((TMessage)msg));
        }

        // 注销全局域 S2C 协议 Handler
        public void Unregister(Type messageType)
        {
            if (messageType == null)
            {
                Debug.LogError("[ClientGlobalMessageRouter] Unregister 失败：messageType 不得为 null");
                return;
            }

            if (!_handlers.ContainsKey(messageType))
            {
                Debug.LogWarning(
                    $"[ClientGlobalMessageRouter] Unregister 警告：协议类型 {messageType.Name} " +
                    $"未注册，本次注销已忽略。");
                return;
            }

            _handlers.Remove(messageType);
        }

        // 泛型注销重载
        public void Unregister<TMessage>() where TMessage : S2CGlobalMessage
        {
            Unregister(typeof(TMessage));
        }

        // 分发全局域 S2C 消息到对应主处理 Handler
        public void Dispatch(S2CGlobalMessage message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientGlobalMessageRouter] Dispatch 失败：message 不得为 null");
                return;
            }

            var messageType = message.GetType();

            if (!_handlers.TryGetValue(messageType, out var handler))
            {
                Debug.LogWarning(
                    $"[ClientGlobalMessageRouter] 未找到协议类型 {messageType.Name} 的主处理 Handler，" +
                    $"消息已丢弃。请确认对应客户端模块已完成 Handler 注册。");
                return;
            }

            handler.Invoke(message);
        }

        // 清空全部 Handler 注册
        public void Clear()
        {
            _handlers.Clear();
        }

        // 当前已注册 Handler 数量，用于诊断
        public int RegisteredHandlerCount => _handlers.Count;
    }
}
