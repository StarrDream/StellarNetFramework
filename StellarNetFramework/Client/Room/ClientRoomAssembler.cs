using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Client.Room
{
    /// <summary>
    /// 客户端实例级房间装配器。
    /// 负责客户端房间组件的实例化、初始化、Router 绑定与装配失败原子回滚。
    /// 严格落实文档 24.4 节：生命周期绑定到具体 ClientRoomInstance，不得作为全局常驻单例。
    /// </summary>
    public sealed class ClientRoomAssembler
    {
        public interface IInitializableClientRoomComponent : IClientRoomComponent
        {
            bool Init(ClientRoomInstance roomInstance);
            void Deinit();
            IReadOnlyList<ClientRoomHandlerBinding> GetHandlerBindings();
            string ComponentId { get; }
        }

        public sealed class ClientRoomHandlerBinding
        {
            public Type MessageType;
            public Action<string, object> Handler;
        }

        private sealed class AssembleRecord
        {
            public IInitializableClientRoomComponent Component;
            public bool IsInitialized;
            public List<Type> RegisteredMessageTypes = new List<Type>();
        }

        // 客户端组件工厂委托委托签名
        public delegate IClientRoomComponent ClientComponentFactory(ClientRoomInstance roomInstance);

        // 客户端组件注册表（简化版，实际项目中可移至独立 Registry 类）
        private readonly Dictionary<string, ClientComponentFactory> _componentRegistry;

        public ClientRoomAssembler(Dictionary<string, ClientComponentFactory> registry)
        {
            if (registry == null)
            {
                Debug.LogError("[ClientRoomAssembler] 构造失败：registry 为 null。");
                return;
            }

            _componentRegistry = registry;
        }

        /// <summary>
        /// 执行客户端房间装配流程。
        /// 任一步骤失败立即触发原子回滚，回滚完成后返回 false。
        /// </summary>
        public bool Assemble(ClientRoomInstance room, string[] componentIds)
        {
            if (room == null)
            {
                Debug.LogError("[ClientRoomAssembler] Assemble 失败：room 为 null。");
                return false;
            }

            if (componentIds == null || componentIds.Length == 0)
            {
                Debug.LogWarning($"[ClientRoomAssembler] Assemble 警告：componentIds 为空，RoomId={room.RoomId}。");
                room.MarkRunning();
                return true;
            }

            var assembleRecords = new List<AssembleRecord>();

            for (int i = 0; i < componentIds.Length; i++)
            {
                string componentId = componentIds[i];

                if (!_componentRegistry.TryGetValue(componentId, out var factory))
                {
                    Debug.LogError($"[ClientRoomAssembler] 组件实例化失败：未找到 {componentId} 的注册工厂，触发原子回滚。");
                    Rollback(room, assembleRecords);
                    return false;
                }

                var rawComponent = factory.Invoke(room);
                var component = rawComponent as IInitializableClientRoomComponent;
                if (component == null)
                {
                    Debug.LogError(
                        $"[ClientRoomAssembler] 组件 {componentId} 未实现 IInitializableClientRoomComponent，触发原子回滚。");
                    Rollback(room, assembleRecords);
                    return false;
                }

                var record = new AssembleRecord { Component = component };

                // 初始化
                bool initSuccess = component.Init(room);
                if (!initSuccess)
                {
                    Debug.LogError($"[ClientRoomAssembler] 组件 {componentId} 初始化失败，触发原子回滚。");
                    Rollback(room, assembleRecords);
                    return false;
                }

                record.IsInitialized = true;

                // 绑定 Router
                var bindings = component.GetHandlerBindings();
                if (bindings != null)
                {
                    foreach (var binding in bindings)
                    {
                        bool registerSuccess = room.MessageRouter.Register(binding.MessageType, binding.Handler);
                        if (!registerSuccess)
                        {
                            Debug.LogError($"[ClientRoomAssembler] 协议 {binding.MessageType.Name} Router 绑定失败，触发原子回滚。");
                            Rollback(room, assembleRecords);
                            return false;
                        }

                        record.RegisteredMessageTypes.Add(binding.MessageType);
                    }
                }

                room.AddComponent(component);
                assembleRecords.Add(record);
            }

            room.MarkRunning();
            Debug.Log($"[ClientRoomAssembler] 客户端房间装配成功，RoomId={room.RoomId}，组件数量={assembleRecords.Count}。");
            return true;
        }

        private void Rollback(ClientRoomInstance room, List<AssembleRecord> records)
        {
            Debug.Log($"[ClientRoomAssembler] 开始原子回滚，RoomId={room.RoomId}。");
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var record = records[i];
                for (int j = record.RegisteredMessageTypes.Count - 1; j >= 0; j--)
                {
                    room.MessageRouter.Unregister(record.RegisteredMessageTypes[j]);
                }

                if (record.IsInitialized)
                {
                    record.Component.Deinit();
                }

                room.RemoveComponent(record.Component);
            }

            Debug.Log($"[ClientRoomAssembler] 原子回滚完成，RoomId={room.RoomId}。");
        }
    }
}