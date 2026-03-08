using System;
using StellarNet.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Client.Network
{
    /// <summary>
    /// 客户端全局域消息注册器，是全局域 Handle 向 ClientGlobalMessageRouter 注册处理委托的统一入口。
    /// 提供泛型链式注册接口，屏蔽 Router 内部的 Type 键操作，防止业务层直接持有 Router 引用。
    /// 只允许注册 S2CGlobalMessage 类型。
    /// </summary>
    public sealed class ClientGlobalMessageRegistrar
    {
        private readonly ClientGlobalMessageRouter _router;

        /// <summary>
        /// 当前注册器是否处于可用状态。
        /// 构造器内部若关键依赖为空，则该对象仍可能被外层持有，因此必须提供显式可用性判断。
        /// </summary>
        public bool IsAvailable => _router != null;

        public ClientGlobalMessageRegistrar(ClientGlobalMessageRouter router)
        {
            if (router == null)
            {
                Debug.LogError("[ClientGlobalMessageRegistrar] 构造失败：router 为 null。");
                return;
            }

            _router = router;
        }

        public ClientGlobalMessageRegistrar Register<TMessage>(Action<TMessage> handler)
            where TMessage : S2CGlobalMessage
        {
            if (_router == null)
            {
                Debug.LogError(
                    $"[ClientGlobalMessageRegistrar] Register 失败：内部 router 为 null，消息类型={typeof(TMessage).Name}。");
                return this;
            }

            if (handler == null)
            {
                Debug.LogError(
                    $"[ClientGlobalMessageRegistrar] Register 失败：handler 为 null，消息类型={typeof(TMessage).Name}。");
                return this;
            }

            _router.Register(typeof(TMessage), raw =>
            {
                var typed = raw as TMessage;
                if (typed == null)
                {
                    Debug.LogError(
                        $"[ClientGlobalMessageRegistrar] 类型转换失败：期望 {typeof(TMessage).Name}，实际={raw?.GetType().Name}。");
                    return;
                }

                handler.Invoke(typed);
            });

            return this;
        }

        public ClientGlobalMessageRegistrar Unregister<TMessage>()
            where TMessage : S2CGlobalMessage
        {
            if (_router == null)
            {
                Debug.LogError(
                    $"[ClientGlobalMessageRegistrar] Unregister 失败：内部 router 为 null，消息类型={typeof(TMessage).Name}。");
                return this;
            }

            _router.Unregister(typeof(TMessage));
            return this;
        }
    }
}