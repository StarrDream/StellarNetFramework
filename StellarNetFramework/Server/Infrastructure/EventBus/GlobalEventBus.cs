// Assets/StellarNetFramework/Server/Infrastructure/EventBus/GlobalEventBus.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Events;
using StellarNet.Server.Infrastructure.GlobalScope;

namespace StellarNet.Server.Infrastructure.EventBus
{
    // 服务端全局域领域事件总线。
    // 由 GlobalInfrastructure 持有，生命周期与服务端全局基础设施一致。
    // 只负责服务端全局域模块之间的领域事件传播，不承担房间域跨房间事件总线职责。
    // 不承担网络协议广播职责，EventBus 只传播处理后的领域结果。
    // 默认采用同步立即派发模型，事件发布后在当前调用链内完成派发。
    // 内置事件计数统计与 Warning 阈值，用于诊断高频热路径误用。
    public sealed class GlobalEventBus : IGlobalService
    {
        // 单类型事件的订阅列表，Key 为事件类型
        private readonly Dictionary<Type, List<Delegate>> _handlers
            = new Dictionary<Type, List<Delegate>>();

        // 事件派发计数器，Key 为事件类型，Value 为当前帧累计派发次数
        private readonly Dictionary<Type, int> _dispatchCounter
            = new Dictionary<Type, int>();

        // 单帧内同一事件类型派发次数超过此阈值时输出 Warning，提示开发者检查热路径误用
        // 默认值 100，可由 GlobalInfrastructure 在初始化时通过构造参数调整
        private readonly int _warningThreshold;

        public GlobalEventBus(int warningThreshold = 100)
        {
            _warningThreshold = warningThreshold;
        }

        // 订阅全局域领域事件。
        // TEvent 必须实现 IGlobalEvent，否则直接报错阻断。
        // 同一 handler 实例重复订阅同一事件类型时，输出 Warning 并忽略，防止重复触发。
        public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IGlobalEvent
        {
            if (handler == null)
            {
                Debug.LogError("[GlobalEventBus] Subscribe 失败：handler 不得为 null，" +
                               $"事件类型：{typeof(TEvent).Name}");
                return;
            }

            var eventType = typeof(TEvent);

            if (!_handlers.TryGetValue(eventType, out var list))
            {
                list = new List<Delegate>();
                _handlers[eventType] = list;
            }

            // 防止同一 handler 重复订阅
            if (list.Contains(handler))
            {
                Debug.LogWarning(
                    $"[GlobalEventBus] 重复订阅警告：handler 已订阅事件 {eventType.Name}，本次订阅已忽略。");
                return;
            }

            list.Add(handler);
        }

        // 取消订阅全局域领域事件。
        // handler 不存在时输出 Warning 并忽略，不视为错误。
        public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IGlobalEvent
        {
            if (handler == null)
            {
                Debug.LogError("[GlobalEventBus] Unsubscribe 失败：handler 不得为 null，" +
                               $"事件类型：{typeof(TEvent).Name}");
                return;
            }

            var eventType = typeof(TEvent);

            if (!_handlers.TryGetValue(eventType, out var list))
                return;

            var removed = list.Remove(handler);
            if (!removed)
            {
                Debug.LogWarning(
                    $"[GlobalEventBus] 取消订阅警告：handler 未在事件 {eventType.Name} 的订阅列表中找到，" +
                    $"本次取消已忽略。");
            }
        }

        // 同步立即派发全局域领域事件。
        // 事件发布后在当前调用链内完成全部 handler 派发，不延迟、不缓冲。
        // 内置派发计数统计，超过 Warning 阈值时输出提示。
        public void Publish<TEvent>(TEvent evt) where TEvent : IGlobalEvent
        {
            if (evt == null)
            {
                Debug.LogError($"[GlobalEventBus] Publish 失败：事件实例不得为 null，" +
                               $"事件类型：{typeof(TEvent).Name}");
                return;
            }

            var eventType = typeof(TEvent);

            // 累计派发计数，超阈值输出 Warning
            if (!_dispatchCounter.TryGetValue(eventType, out var count))
                count = 0;

            count++;
            _dispatchCounter[eventType] = count;

            if (count == _warningThreshold)
            {
                Debug.LogWarning(
                    $"[GlobalEventBus] 高频派发警告：事件 {eventType.Name} 当前帧累计派发次数已达 {count}，" +
                    $"请检查是否存在热路径误用 EventBus 的情况。阈值={_warningThreshold}");
            }

            if (!_handlers.TryGetValue(eventType, out var list) || list.Count == 0)
                return;

            // 使用临时快照派发，防止 handler 内部取消订阅导致列表修改异常
            var snapshot = new List<Delegate>(list);
            foreach (var del in snapshot)
            {
                var handler = del as Action<TEvent>;
                if (handler == null)
                    continue;

                handler.Invoke(evt);
            }
        }

        // 重置当前帧派发计数器，由 GlobalInfrastructure 在每帧 Tick 开始时调用
        public void ResetFrameCounter()
        {
            _dispatchCounter.Clear();
        }

        // 清空当前作用域内的全部订阅关系与待处理事件缓存。
        // Clear() 调用后不得再保留任何旧订阅或旧事件残留。
        // 由 GlobalInfrastructure.Shutdown() 在关停阶段调用。
        public void Clear()
        {
            _handlers.Clear();
            _dispatchCounter.Clear();
        }

        // 获取指定事件类型的当前订阅数量，用于诊断
        public int GetSubscriberCount<TEvent>() where TEvent : IGlobalEvent
        {
            if (_handlers.TryGetValue(typeof(TEvent), out var list))
                return list.Count;
            return 0;
        }
    }
}
