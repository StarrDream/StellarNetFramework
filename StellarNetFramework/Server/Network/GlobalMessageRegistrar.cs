using System;
using StellarNet.Shared.Identity;
using UnityEngine;

namespace StellarNet.Server.Network
{
    /// <summary>
    /// 全局消息注册器，为全局业务 Handle 提供统一的链式注册语义。
    /// 目的是让开发者接触到统一注册风格、最少理解成本，隐藏 Router 内部实现细节。
    /// 支持链式 Register 风格：registrar.Register&lt;C2S_XXX&gt;(OnC2S_XXX).Register&lt;C2S_YYY&gt;(OnC2S_YYY)
    /// 全局业务 Handle 在 RegisterAll() 入口中集中调用，不允许拆散到多个位置。
    /// </summary>
    public sealed class GlobalMessageRegistrar
    {
        private readonly ServerGlobalMessageRouter _router;

        public GlobalMessageRegistrar(ServerGlobalMessageRouter router)
        {
            if (router == null)
            {
                Debug.LogError("[GlobalMessageRegistrar] 构造失败：router 为 null。");
                return;
            }
            _router = router;
        }

        /// <summary>
        /// 注册全局域协议处理方法，返回自身以支持链式调用。
        /// 处理方法签名：(ConnectionId connectionId, TMessage message)
        /// 使用示例：registrar.Register&lt;C2S_Login&gt;(OnC2S_Login).Register&lt;C2S_Reconnect&gt;(OnC2S_Reconnect)
        /// </summary>
        public GlobalMessageRegistrar Register<TMessage>(Action<ConnectionId, TMessage> handler)
            where TMessage : class
        {
            if (handler == null)
            {
                Debug.LogError($"[GlobalMessageRegistrar] Register 失败：handler 为 null，消息类型={typeof(TMessage).Name}。");
                return this;
            }

            if (_router == null)
            {
                Debug.LogError($"[GlobalMessageRegistrar] Register 失败：内部 router 为 null，消息类型={typeof(TMessage).Name}。");
                return this;
            }

            // 将强类型委托包装为 Router 接受的 (ConnectionId, object) 委托，保持 Router 内部类型无关
            void WrappedHandler(ConnectionId connectionId, object rawMessage)
            {
                var typedMessage = rawMessage as TMessage;
                if (typedMessage == null)
                {
                    Debug.LogError($"[GlobalMessageRegistrar] 消息类型转换失败：期望 {typeof(TMessage).Name}，实际 {rawMessage?.GetType().Name}，ConnectionId={connectionId}，已丢弃。");
                    return;
                }
                handler.Invoke(connectionId, typedMessage);
            }

            _router.Register(typeof(TMessage), WrappedHandler);
            return this;
        }

        /// <summary>
        /// 注销全局域协议处理方法，返回自身以支持链式调用。
        /// 在业务 Handle 生命周期结束时调用，防止业务单元停用后仍保留监听。
        /// </summary>
        public GlobalMessageRegistrar Unregister<TMessage>()
            where TMessage : class
        {
            if (_router == null)
            {
                Debug.LogError($"[GlobalMessageRegistrar] Unregister 失败：内部 router 为 null，消息类型={typeof(TMessage).Name}。");
                return this;
            }

            _router.Unregister(typeof(TMessage));
            return this;
        }
    }
}
