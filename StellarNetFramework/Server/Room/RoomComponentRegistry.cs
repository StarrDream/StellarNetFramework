using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Server.Room
{
    /// <summary>
    /// 房间业务组件注册表，维护稳定组件注册标识到组件工厂的映射。
    /// 使用稳定组件注册标识（字符串常量），不使用运行时类型名，保证回放文件头中的 RoomComponentIds 跨版本稳定。
    /// 组件工厂负责创建组件实例，装配器负责初始化与 Router 绑定。
    /// 此注册表在服务端启动阶段由 GlobalInfrastructure 显式构建，运行期禁止动态覆盖。
    /// </summary>
    public sealed class RoomComponentRegistry
    {
        /// <summary>
        /// 组件工厂委托，接收 RoomInstance 上下文，返回已创建（但未初始化）的组件实例。
        /// </summary>
        public delegate IRoomComponent ComponentFactory(RoomInstance roomInstance);

        // 稳定组件注册标识 → 组件工厂
        private readonly Dictionary<string, ComponentFactory> _factories
            = new Dictionary<string, ComponentFactory>(StringComparer.Ordinal);

        /// <summary>
        /// 注册组件工厂。
        /// componentId 必须是稳定的字符串常量，不得使用运行时类型名（typeof(T).Name）。
        /// 重复注册同一 componentId 时直接报错阻断，防止注册表污染。
        /// </summary>
        public void Register(string componentId, ComponentFactory factory)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                Debug.LogError("[RoomComponentRegistry] Register 失败：componentId 为空，组件注册标识必须是稳定的字符串常量。");
                return;
            }

            if (factory == null)
            {
                Debug.LogError($"[RoomComponentRegistry] Register 失败：factory 为 null，componentId={componentId}。");
                return;
            }

            if (_factories.ContainsKey(componentId))
            {
                Debug.LogError($"[RoomComponentRegistry] Register 失败：componentId={componentId} 已存在注册，禁止重复注册，请检查启动装配顺序。");
                return;
            }

            _factories[componentId] = factory;
            Debug.Log($"[RoomComponentRegistry] 组件注册成功，componentId={componentId}。");
        }

        /// <summary>
        /// 通过稳定组件注册标识创建组件实例。
        /// 找不到对应工厂时返回 null，调用方（ServerRoomAssembler）必须做判空处理并阻断装配流程。
        /// </summary>
        public IRoomComponent CreateComponent(string componentId, RoomInstance roomInstance)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                Debug.LogError($"[RoomComponentRegistry] CreateComponent 失败：componentId 为空，RoomId={roomInstance?.RoomId}。");
                return null;
            }

            if (roomInstance == null)
            {
                Debug.LogError($"[RoomComponentRegistry] CreateComponent 失败：roomInstance 为 null，componentId={componentId}。");
                return null;
            }

            if (!_factories.TryGetValue(componentId, out var factory))
            {
                Debug.LogError($"[RoomComponentRegistry] CreateComponent 失败：componentId={componentId} 未在注册表中找到对应工厂，" +
                               $"RoomId={roomInstance.RoomId}，请检查是否遗漏注册或使用了非稳定标识。");
                return null;
            }

            IRoomComponent component = factory.Invoke(roomInstance);

            if (component == null)
            {
                Debug.LogError($"[RoomComponentRegistry] CreateComponent 失败：工厂返回 null，componentId={componentId}，RoomId={roomInstance.RoomId}。");
                return null;
            }

            return component;
        }

        /// <summary>
        /// 判断指定组件标识是否已注册。
        /// </summary>
        public bool IsRegistered(string componentId)
        {
            return !string.IsNullOrEmpty(componentId) && _factories.ContainsKey(componentId);
        }
    }
}
