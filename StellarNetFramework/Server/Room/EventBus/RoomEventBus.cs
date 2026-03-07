// Assets/StellarNetFramework/Server/Room/EventBus/RoomEventBus.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Events;

namespace StellarNet.Server.Room.EventBus
{
    // 服务端房间域领域事件总线。
    // 每个 RoomInstance 持有自己独立的 RoomEventBus 实例，与 GlobalEventBus 物理隔离。
    // 只负责房间域内模块之间的领域事件传播，不承担跨房间事件总线职责。
    // 不承担网络协议广播职责，EventBus 只传播处理后的领域结果。
    // 默认采用同步立即派发模型，事件发布后在当前调用链内完成派发。
    // 内置事件计数统计与 Warning 阈值，用于诊断高频热路径误用。
    public sealed class RoomEventBus
    {
        // 所属房间 ID，用于错误日志定位
        private readonly string _roomId;

        // 单类型事件的订阅列表，Key 为事件类型
        private readonly Dictionary<Type, List<Delegate>> _handlers
            = new Dictionary<Type, List<Delegate>>();

        // 事件派发计数器，Key 为事件类型，Value 为当前帧累计派发次数
        private readonly Dictionary<Type, int> _dispatchCounter
            = new Dictionary<Type, int>();

        // 单帧内同一事件类型派发次数超过此阈值时输出 Warning
        private readonly int _warningThreshold;

        public RoomEventBus(string roomId, int warningThreshold = 100)
        {
            _roomId = roomId ?? string.Empty;
            _warningThreshold = warningThreshold;
        }

        // 订阅房间域领域事件。
        // TEvent 必须实现 IRoomEvent，否则直接报错阻断。
        // 同一 handler 实例重复订阅同一事件类型时，输出 Warning 并忽略。
        public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IRoomEvent
        {
            if (handler == null)
            {
                Debug.LogError(
                    $"[RoomEventBus] RoomId={_roomId} Subscribe 失败：handler 不得为 null，" +
                    $"事件类型：{typeof(TEvent).Name}");
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
                Debug.LogWarning(
                    $"[RoomEventBus] RoomId={_roomId} 重复订阅警告：" +
                    $"handler 已订阅事件 {eventType.Name}，本次订阅已忽略。");
                return;
            }

            list.Add(handler);
        }

        // 取消订阅房间域领域事件。
        // handler 不存在时输出 Warning 并忽略，不视为错误。
        public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IRoomEvent
        {
            if (handler == null)
            {
                Debug.LogError(
                    $"[RoomEventBus] RoomId={_roomId} Unsubscribe 失败：handler 不得为 null，" +
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
                    $"[RoomEventBus] RoomId={_roomId} 取消订阅警告：" +
                    $"handler 未在事件 {eventType.Name} 的订阅列表中找到，本次取消已忽略。");
            }
        }

        // 同步立即派发房间域领域事件。
        // 事件发布后在当前调用链内完成全部 handler 派发，不延迟、不缓冲。
        public void Publish<TEvent>(TEvent evt) where TEvent : IRoomEvent
        {
            if (evt == null)
            {
                Debug.LogError(
                    $"[RoomEventBus] RoomId={_roomId} Publish 失败：事件实例不得为 null，" +
                    $"事件类型：{typeof(TEvent).Name}");
                return;
            }

            var eventType = typeof(TEvent);

            if (!_dispatchCounter.TryGetValue(eventType, out var count))
                count = 0;

            count++;
            _dispatchCounter[eventType] = count;

            if (count == _warningThreshold)
            {
                Debug.LogWarning(
                    $"[RoomEventBus] RoomId={_roomId} 高频派发警告：" +
                    $"事件 {eventType.Name} 当前帧累计派发次数已达 {count}，" +
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

        // 重置当前帧派发计数器，由 RoomInstance 在每帧 Tick 开始时调用
        public void ResetFrameCounter()
        {
            _dispatchCounter.Clear();
        }

        // 清空当前作用域内的全部订阅关系。
        // Clear() 调用后不得再保留任何旧订阅或旧事件残留。
        // 由 RoomInstance 销毁流程第三步调用。
        public void Clear()
        {
            _handlers.Clear();
            _dispatchCounter.Clear();
        }

        // 获取指定事件类型的当前订阅数量，用于诊断
        public int GetSubscriberCount<TEvent>() where TEvent : IRoomEvent
        {
            if (_handlers.TryGetValue(typeof(TEvent), out var list))
                return list.Count;
            return 0;
        }
    }
}
