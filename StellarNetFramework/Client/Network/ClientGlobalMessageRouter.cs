using System;
using System.Collections.Generic;
using StellarNet.Shared.Registry;
using UnityEngine;

namespace StellarNet.Client.Network
{
    /// <summary>
    /// 客户端全局域消息路由器，管理全局域协议到 Handle 的注册表并完成分发。
    /// 职责严格限定为：按消息类型查找全局模块主处理者并调用，不承担业务实现。
    /// 全局业务 Handle 通过 ClientGlobalMessageRegistrar 向此路由器注册处理委托。
    /// </summary>
    public sealed class ClientGlobalMessageRouter
    {
        private readonly Dictionary<Type, Action<object>> _handlers
            = new Dictionary<Type, Action<object>>();

        /// <summary>
        /// 注册全局域协议处理委托。
        /// 同一协议类型只允许存在一个主处理委托，重复注册直接报错阻断。
        /// </summary>
        public void Register(Type messageType, Action<object> handler)
        {
            if (messageType == null)
            {
                Debug.LogError("[ClientGlobalMessageRouter] Register 失败：messageType 为 null。");
                return;
            }
            if (handler == null)
            {
                Debug.LogError($"[ClientGlobalMessageRouter] Register 失败：handler 为 null，messageType={messageType.Name}。");
                return;
            }
            if (_handlers.ContainsKey(messageType))
            {
                Debug.LogError($"[ClientGlobalMessageRouter] Register 失败：协议类型 {messageType.Name} 已存在主处理委托，禁止重复注册。");
                return;
            }

            _handlers[messageType] = handler;
        }

        /// <summary>
        /// 注销全局域协议处理委托。
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
        /// 分发全局域消息到对应的主处理委托。
        /// 找不到处理者时输出 Warning，不影响其他消息处理。
        /// </summary>
        public void Dispatch(MessageMetadata metadata, object message)
        {
            if (metadata == null)
            {
                Debug.LogError("[ClientGlobalMessageRouter] Dispatch 失败：metadata 为 null。");
                return;
            }
            if (message == null)
            {
                Debug.LogError($"[ClientGlobalMessageRouter] Dispatch 失败：message 为 null，MessageId={metadata.MessageId}。");
                return;
            }

            if (!_handlers.TryGetValue(metadata.MessageType, out var handler))
            {
                Debug.LogWarning($"[ClientGlobalMessageRouter] 未找到协议 {metadata.MessageType?.Name}（MessageId={metadata.MessageId}）的处理者，消息已忽略。");
                return;
            }

            handler.Invoke(message);
        }
    }
}
