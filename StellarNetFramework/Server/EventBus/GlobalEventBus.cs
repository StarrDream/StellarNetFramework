using System;
using System.Collections.Generic;
using StellarNet.Shared.EventBus;
using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.EventBus
{
    /// <summary>
    /// 服务端全局域事件总线，负责全局域模块之间的领域事件解耦传播。
    /// 必须由 GlobalInfrastructure 持有，与 GlobalScope ServiceLocator 处于同一服务端全局作用域层级。
    /// 只允许处理实现了 IGlobalEvent 的事件类型，防止跨域事件误投递。
    /// 默认采用同步立即派发模型，框架层不依赖延迟派发才能正确工作。
    /// 不承担网络协议广播职责，不直接承担原始协议广播职责。
    /// 网络协议类型不得直接作为 EventBus 事件类型使用。
    /// </summary>
    public sealed class GlobalEventBus : IGlobalService
    {
        // 事件类型 → 订阅委托列表
        private readonly Dictionary<Type, List<Delegate>> _handlers
            = new Dictionary<Type, List<Delegate>>();

        /// <summary>
        /// 订阅全局域领域事件。
        /// 只允许订阅实现了 IGlobalEvent 的事件类型，防止跨域事件误投递。
        /// 同一委托重复订阅时输出 Warning，不重复添加。
        /// </summary>
        public void Subscribe<TEvent>(Action<TEvent> handler)
            where TEvent : class, IGlobalEvent
        {
            if (handler == null)
            {
                Debug.LogError($"[GlobalEventBus] Subscribe 失败：handler 为 null，事件类型={typeof(TEvent).Name}。");
                return;
            }

            var eventType = typeof(TEvent);
            if (!_handlers.TryGetValue(eventType, out var list))
            {
                list = new List<Delegate>();
                _handlers[eventType] = list;
            }

            if (list.Contains(handler))
            {
                Debug.LogWarning($"[GlobalEventBus] Subscribe 警告：事件类型 {typeof(TEvent).Name} 的同一委托已存在，不重复添加。");
                return;
            }

            list.Add(handler);
        }

        /// <summary>
        /// 取消订阅全局域领域事件。
        /// 在业务单元生命周期结束时必须调用，防止业务单元停用后仍保留监听。
        /// </summary>
        public void Unsubscribe<TEvent>(Action<TEvent> handler)
            where TEvent : class, IGlobalEvent
        {
            if (handler == null)
            {
                return;
            }

            var eventType = typeof(TEvent);
            if (!_handlers.TryGetValue(eventType, out var list))
            {
                return;
            }

            list.Remove(handler);
        }

        /// <summary>
        /// 发布全局域领域事件，采用同步立即派发模型。
        /// 发布后在当前调用链内完成所有订阅者的派发，不依赖延迟派发。
        /// 只允许发布实现了 IGlobalEvent 的事件类型。
        /// </summary>
        public void Publish<TEvent>(TEvent evt)
            where TEvent : class, IGlobalEvent
        {
            if (evt == null)
            {
                Debug.LogError($"[GlobalEventBus] Publish 失败：事件实例为 null，事件类型={typeof(TEvent).Name}。");
                return;
            }

            var eventType = typeof(TEvent);
            if (!_handlers.TryGetValue(eventType, out var list) || list.Count == 0)
            {
                // 无订阅者属于正常情况，不输出 Error
                return;
            }

            // 快照当前订阅列表，防止派发过程中订阅列表被修改导致迭代异常
            var snapshot = new List<Delegate>(list);
            foreach (var del in snapshot)
            {
                var handler = del as Action<TEvent>;
                if (handler == null)
                {
                    Debug.LogError($"[GlobalEventBus] 派发失败：委托类型转换异常，事件类型={typeof(TEvent).Name}。");
                    continue;
                }
                handler.Invoke(evt);
            }
        }

        /// <summary>
        /// 清空当前作用域内的全部订阅关系。
        /// 语义固定为：清空所有订阅关系，若存在待处理事件缓存也一并清空。
        /// 调用后该作用域内不得再保留任何旧订阅或旧事件残留。
        /// 由 GlobalInfrastructure.Shutdown() 统一调用。
        /// </summary>
        public void Clear()
        {
            _handlers.Clear();
        }
    }
}
