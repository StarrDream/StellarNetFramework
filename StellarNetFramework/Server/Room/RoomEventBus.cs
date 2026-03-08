using System;
using System.Collections.Generic;
using StellarNet.Shared.EventBus;
using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.Room
{
    /// <summary>
    /// 房间域事件总线，负责单个房间内业务组件之间的领域事件解耦传播。
    /// 每个 RoomInstance 持有自己独立的 RoomEventBus 实例，与 GlobalEventBus 物理隔离。
    /// 只允许处理实现了 IRoomEvent 的事件类型，防止跨域事件误投递。
    /// 默认采用同步立即派发模型，框架层不依赖延迟派发才能正确工作。
    /// 房间域结果若需上升到服务端全局域，必须由明确的 Handle 或 Service 转译后再进入 GlobalEventBus。
    /// 不承担网络协议广播职责。
    /// </summary>
    public sealed class RoomEventBus : IRoomService
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers
            = new Dictionary<Type, List<Delegate>>();

        // 所属房间 RoomId，用于日志诊断
        private readonly string _roomId;

        public RoomEventBus(string roomId)
        {
            _roomId = roomId ?? string.Empty;
        }

        /// <summary>
        /// 订阅房间域领域事件。
        /// 只允许订阅实现了 IRoomEvent 的事件类型，防止跨域事件误投递。
        /// 同一委托重复订阅时输出 Warning，不重复添加。
        /// </summary>
        public void Subscribe<TEvent>(Action<TEvent> handler)
            where TEvent : class, IRoomEvent
        {
            if (handler == null)
            {
                Debug.LogError($"[RoomEventBus] Subscribe 失败：handler 为 null，事件类型={typeof(TEvent).Name}，RoomId={_roomId}。");
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
                Debug.LogWarning($"[RoomEventBus] Subscribe 警告：事件类型 {typeof(TEvent).Name} 的同一委托已存在，RoomId={_roomId}，不重复添加。");
                return;
            }

            list.Add(handler);
        }

        /// <summary>
        /// 取消订阅房间域领域事件。
        /// 在房间业务组件销毁前必须调用，确保房间销毁后不残留旧监听。
        /// </summary>
        public void Unsubscribe<TEvent>(Action<TEvent> handler)
            where TEvent : class, IRoomEvent
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
        /// 发布房间域领域事件，采用同步立即派发模型。
        /// 发布后在当前调用链内完成所有订阅者的派发，不依赖延迟派发。
        /// 只允许发布实现了 IRoomEvent 的事件类型。
        /// </summary>
        public void Publish<TEvent>(TEvent evt)
            where TEvent : class, IRoomEvent
        {
            if (evt == null)
            {
                Debug.LogError($"[RoomEventBus] Publish 失败：事件实例为 null，事件类型={typeof(TEvent).Name}，RoomId={_roomId}。");
                return;
            }

            var eventType = typeof(TEvent);
            if (!_handlers.TryGetValue(eventType, out var list) || list.Count == 0)
            {
                return;
            }

            var snapshot = new List<Delegate>(list);
            foreach (var del in snapshot)
            {
                var handler = del as Action<TEvent>;
                if (handler == null)
                {
                    Debug.LogError($"[RoomEventBus] 派发失败：委托类型转换异常，事件类型={typeof(TEvent).Name}，RoomId={_roomId}。");
                    continue;
                }
                handler.Invoke(evt);
            }
        }

        /// <summary>
        /// 清空当前房间作用域内的全部订阅关系。
        /// 语义固定为：清空所有订阅关系，调用后不得再保留任何旧订阅残留。
        /// 由 RoomInstance 在 OnRoomDestroy 阶段统一调用。
        /// </summary>
        public void Clear()
        {
            _handlers.Clear();
        }
    }
}
