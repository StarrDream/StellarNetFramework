using System;
using System.Collections.Generic;
using StellarNet.Shared.Identity;
using UnityEngine;

namespace StellarNet.Server.Network
{
    /// <summary>
    /// 房间消息注册器，为房间业务 RoomHandle 提供统一的链式注册语义。
    /// 每个 RoomInstance 持有自己的 RoomMessageRegistrar 实例，与 ServerRoomMessageRouter 一一对应。
    /// 注册/注销发起方必须是 ServerRoomAssembler，房间业务组件自身不得直接调用此注册器。
    /// 支持链式 Register 风格。
    /// </summary>
    public sealed class RoomMessageRegistrar
    {
        private readonly ServerRoomMessageRouter _router;

        // 记录消息类型对应的包装委托，确保注销时使用与注册时完全一致的引用
        private readonly Dictionary<Type, Action<ConnectionId, string, object>> _wrappedHandlers
            = new Dictionary<Type, Action<ConnectionId, string, object>>();

        public RoomMessageRegistrar(ServerRoomMessageRouter router)
        {
            if (router == null)
            {
                Debug.LogError("[RoomMessageRegistrar] 构造失败：router 为 null。");
                return;
            }

            _router = router;
        }

        /// <summary>
        /// 动态注册房间域协议处理方法。
        /// </summary>
        public bool Register<TMessage>(Action<ConnectionId, string, TMessage> handler)
            where TMessage : class
        {
            if (handler == null)
            {
                Debug.LogError($"[RoomMessageRegistrar] Register 失败：handler 为 null，消息类型={typeof(TMessage).Name}。");
                return false;
            }

            if (_router == null)
            {
                Debug.LogError($"[RoomMessageRegistrar] Register 失败：内部 router 为 null，消息类型={typeof(TMessage).Name}。");
                return false;
            }

            Type messageType = typeof(TMessage);
            if (_wrappedHandlers.ContainsKey(messageType))
            {
                Debug.LogError($"[RoomMessageRegistrar] Register 失败：消息类型 {messageType.Name} 已在本注册器中注册，禁止重复注册。");
                return false;
            }

            void WrappedHandler(ConnectionId connectionId, string roomId, object rawMessage)
            {
                var typedMessage = rawMessage as TMessage;
                if (typedMessage == null)
                {
                    Debug.LogError(
                        $"[RoomMessageRegistrar] 消息类型转换失败：期望 {messageType.Name}，实际 {rawMessage?.GetType().Name}，ConnectionId={connectionId}，RoomId={roomId}，已丢弃。");
                    return;
                }

                handler.Invoke(connectionId, roomId, typedMessage);
            }

            bool success = _router.Register(messageType, WrappedHandler);
            if (!success)
            {
                return false;
            }

            _wrappedHandlers[messageType] = WrappedHandler;
            return true;
        }

        /// <summary>
        /// 动态注销房间域协议处理方法。
        /// </summary>
        public RoomMessageRegistrar Unregister<TMessage>()
            where TMessage : class
        {
            if (_router == null)
            {
                Debug.LogError($"[RoomMessageRegistrar] Unregister 失败：内部 router 为 null，消息类型={typeof(TMessage).Name}。");
                return this;
            }

            Type messageType = typeof(TMessage);
            if (!_wrappedHandlers.TryGetValue(messageType, out var wrappedHandler))
            {
                Debug.LogWarning($"[RoomMessageRegistrar] Unregister 警告：消息类型 {messageType.Name} 未在本注册器中找到包装委托记录。");
                return this;
            }

            _router.UnregisterByExactHandler(messageType, wrappedHandler);
            _wrappedHandlers.Remove(messageType);
            return this;
        }

        /// <summary>
        /// 清空所有注册记录，由 ServerRoomAssembler 在房间销毁或装配回滚时调用。
        /// </summary>
        public void ClearAll()
        {
            _router?.ClearAll();
            _wrappedHandlers.Clear();
        }
    }
}