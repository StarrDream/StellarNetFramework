// Assets/StellarNetFramework/Server/Network/Router/ServerGlobalMessageRouter.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.Base;

namespace StellarNet.Server.Network.Router
{
    // 服务端全局域消息路由器，管理全局域协议到 Handler 的注册表与分发。
    // 职责严格限定为：按消息类型查找全局模块主处理者并调用，不负责业务实现。
    // 同一协议类型只允许存在一个主处理 Handler，重复注册直接报错阻断。
    // Handler 注册由各全局模块在 GlobalInfrastructure 装配阶段完成，不允许运行期随意覆盖。
    public sealed class ServerGlobalMessageRouter
    {
        // 协议 Type → Handler 委托映射表
        // Handler 签名：(ConnectionId 来源连接, C2SGlobalMessage 消息体)
        private readonly Dictionary<Type, Action<ConnectionId, C2SGlobalMessage>> _handlers
            = new Dictionary<Type, Action<ConnectionId, C2SGlobalMessage>>();

        // 注册全局域协议 Handler。
        // 参数 messageType：协议运行时类型，必须继承自 C2SGlobalMessage。
        // 参数 handler：主处理委托，不得为 null。
        // 重复注册同一类型时直接报错阻断，不允许静默覆盖。
        public void Register(Type messageType, Action<ConnectionId, C2SGlobalMessage> handler)
        {
            if (messageType == null)
            {
                Debug.LogError("[ServerGlobalMessageRouter] Register 失败：messageType 不得为 null");
                return;
            }

            if (handler == null)
            {
                Debug.LogError(
                    $"[ServerGlobalMessageRouter] Register 失败：handler 不得为 null，" +
                    $"Type={messageType.Name}");
                return;
            }

            if (!typeof(C2SGlobalMessage).IsAssignableFrom(messageType))
            {
                Debug.LogError(
                    $"[ServerGlobalMessageRouter] Register 失败：类型 {messageType.Name} " +
                    $"未继承 C2SGlobalMessage，全局域路由器只接受 C2SGlobalMessage 子类型。");
                return;
            }

            if (_handlers.ContainsKey(messageType))
            {
                Debug.LogError(
                    $"[ServerGlobalMessageRouter] Register 冲突：协议类型 {messageType.Name} " +
                    $"已存在主处理 Handler，同一协议类型只允许一个主处理者，当前注册已阻断。");
                return;
            }

            _handlers[messageType] = handler;
        }

        // 泛型注册重载
        public void Register<TMessage>(Action<ConnectionId, TMessage> handler)
            where TMessage : C2SGlobalMessage
        {
            if (handler == null)
            {
                Debug.LogError(
                    $"[ServerGlobalMessageRouter] Register 失败：handler 不得为 null，" +
                    $"Type={typeof(TMessage).Name}");
                return;
            }

            Register(typeof(TMessage), (connId, msg) => handler(connId, (TMessage)msg));
        }

        // 注销全局域协议 Handler，用于模块反初始化阶段
        public void Unregister(Type messageType)
        {
            if (messageType == null)
            {
                Debug.LogError("[ServerGlobalMessageRouter] Unregister 失败：messageType 不得为 null");
                return;
            }

            if (!_handlers.ContainsKey(messageType))
            {
                Debug.LogWarning(
                    $"[ServerGlobalMessageRouter] Unregister 警告：协议类型 {messageType.Name} " +
                    $"未注册，本次注销已忽略。");
                return;
            }

            _handlers.Remove(messageType);
        }

        // 泛型注销重载
        public void Unregister<TMessage>() where TMessage : C2SGlobalMessage
        {
            Unregister(typeof(TMessage));
        }

        // 分发全局域消息到对应主处理 Handler。
        // 未找到 Handler 时输出 Warning，不视为 Fatal 错误，
        // 原因是全局模块可能在特定生命周期阶段尚未注册。
        public void Dispatch(ConnectionId connectionId, C2SGlobalMessage message)
        {
            if (message == null)
            {
                Debug.LogError(
                    $"[ServerGlobalMessageRouter] Dispatch 失败：message 不得为 null，" +
                    $"ConnectionId={connectionId}");
                return;
            }

            var messageType = message.GetType();

            if (!_handlers.TryGetValue(messageType, out var handler))
            {
                Debug.LogWarning(
                    $"[ServerGlobalMessageRouter] 未找到协议类型 {messageType.Name} 的主处理 Handler，" +
                    $"ConnectionId={connectionId}，消息已丢弃。请确认对应全局模块已完成 Handler 注册。");
                return;
            }

            handler.Invoke(connectionId, message);
        }

        // 当前已注册 Handler 数量，用于诊断
        public int RegisteredHandlerCount => _handlers.Count;
    }
}
