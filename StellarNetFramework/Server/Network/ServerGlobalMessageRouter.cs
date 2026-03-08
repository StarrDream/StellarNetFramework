using System;
using System.Collections.Generic;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Registry;
using UnityEngine;

namespace StellarNet.Server.Network
{
    /// <summary>
    /// 服务端全局域消息路由器，管理全局域协议到 Handle 的注册表并完成分发。
    /// 职责严格限定为：按消息类型查找全局模块主处理者并调用，不承担业务实现。
    /// 全局业务 Handle 通过 GlobalMessageRegistrar 向此路由器注册处理委托。
    /// </summary>
    public sealed class ServerGlobalMessageRouter
    {
        // 全局域协议处理委托表：消息 Type → 处理委托
        // 委托签名为 (ConnectionId, object message)，object 为已反序列化的协议实例
        private readonly Dictionary<Type, Action<ConnectionId, object>> _handlers
            = new Dictionary<Type, Action<ConnectionId, object>>();

        /// <summary>
        /// 注册全局域协议处理委托。
        /// 由 GlobalMessageRegistrar 在业务 Handle 初始化阶段调用。
        /// 同一协议类型只允许存在一个主处理委托，重复注册直接报错阻断。
        /// </summary>
        public void Register(Type messageType, Action<ConnectionId, object> handler)
        {
            if (messageType == null)
            {
                Debug.LogError("[ServerGlobalMessageRouter] Register 失败：messageType 为 null。");
                return;
            }
            if (handler == null)
            {
                Debug.LogError($"[ServerGlobalMessageRouter] Register 失败：handler 为 null，messageType={messageType.Name}。");
                return;
            }
            if (_handlers.ContainsKey(messageType))
            {
                Debug.LogError($"[ServerGlobalMessageRouter] Register 失败：协议类型 {messageType.Name} 已存在主处理委托，禁止重复注册，当前注册来源请检查业务 Handle 初始化顺序。");
                return;
            }

            _handlers[messageType] = handler;
        }

        /// <summary>
        /// 注销全局域协议处理委托。
        /// 由 GlobalMessageRegistrar 在业务 Handle 销毁阶段调用，防止业务单元停用后仍保留监听。
        /// </summary>
        public void Unregister(Type messageType)
        {
            if (messageType == null)
            {
                Debug.LogError("[ServerGlobalMessageRouter] Unregister 失败：messageType 为 null。");
                return;
            }
            _handlers.Remove(messageType);
        }

        /// <summary>
        /// 分发全局域消息到对应的主处理委托。
        /// 由 ServerNetworkEntry 在确认协议属于全局域后调用。
        /// 找不到处理者时输出 Warning（不是 Error，允许部分协议暂无处理者），不影响其他消息处理。
        /// </summary>
        public void Dispatch(ConnectionId connectionId, MessageMetadata metadata, object message)
        {
            if (metadata == null)
            {
                Debug.LogError($"[ServerGlobalMessageRouter] Dispatch 失败：metadata 为 null，ConnectionId={connectionId}。");
                return;
            }
            if (message == null)
            {
                Debug.LogError($"[ServerGlobalMessageRouter] Dispatch 失败：message 为 null，MessageId={metadata.MessageId}，ConnectionId={connectionId}。");
                return;
            }

            if (!_handlers.TryGetValue(metadata.MessageType, out var handler))
            {
                Debug.LogWarning($"[ServerGlobalMessageRouter] 未找到协议 {metadata.MessageType?.Name}（MessageId={metadata.MessageId}）的处理者，ConnectionId={connectionId}，消息已忽略。");
                return;
            }

            handler.Invoke(connectionId, message);
        }
    }
}
