using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Server.Room
{
    /// <summary>
    /// 服务端房间业务组件装配器，负责组件实例化、依赖接线、Router 绑定与装配失败原子回滚。
    /// 房间装配逻辑不得直接内嵌在 RoomInstance 本体中。
    /// GlobalRoomManager 或 RoomDispatcher 可触发装配流程，但不直接承担装配细节实现。
    /// 任一组件实例化失败、初始化失败或 Router 绑定失败时，必须中止整个装配流程并执行原子回滚。
    /// 原子回滚要求：已绑定 Router 的 Handle 按装配逆序注销，已初始化的组件按装配逆序反初始化。
    /// 任一回滚步骤失败时仍继续执行后续回滚，确保最终不留脏状态。
    /// 只有在回滚完成后，才允许向上层返回装配失败结果。
    /// 房间业务组件自身不得在 Init/OnDestroy 中主动直接调用 Router 完成注册/注销。
    /// 注册/注销发起方必须是本装配器。
    /// </summary>
    public sealed class ServerRoomAssembler
    {
        /// <summary>
        /// 可初始化房间组件接口，扩展 IRoomComponent，增加初始化与反初始化语义。
        /// 所有需要参与装配流程的房间业务组件必须实现此接口。
        /// </summary>
        public interface IInitializableRoomComponent : IRoomComponent
        {
            /// <summary>
            /// 组件初始化，在 RoomInstance 装配阶段由装配器调用。
            /// 返回 false 表示初始化失败，装配器将立即触发原子回滚。
            /// </summary>
            bool Init(RoomInstance roomInstance);

            /// <summary>
            /// 组件反初始化，在装配回滚或房间销毁时由装配器调用。
            /// 即使反初始化失败也必须继续执行后续回滚步骤，确保不留脏状态。
            /// </summary>
            void Deinit();

            /// <summary>
            /// 获取该组件需要注册到 Router 的协议处理能力描述列表。
            /// 组件只负责声明能力，装配器负责将能力绑定到 Router。
            /// </summary>
            IReadOnlyList<RoomHandlerBinding> GetHandlerBindings();

            /// <summary>
            /// 获取该组件的稳定注册标识，用于 RoomComponentIds 写入。
            /// 必须是稳定的字符串常量，不得使用运行时类型名。
            /// </summary>
            string ComponentId { get; }
        }

        /// <summary>
        /// 房间协议处理能力描述，由组件声明、装配器绑定到 Router。
        /// </summary>
        public sealed class RoomHandlerBinding
        {
            public System.Type MessageType;
            public System.Action<StellarNet.Shared.Identity.ConnectionId, string, object> Handler;
        }

        // 装配过程中的中间状态记录，用于原子回滚
        private sealed class AssembleRecord
        {
            public IInitializableRoomComponent Component;
            public bool IsInitialized;
            public List<System.Type> RegisteredMessageTypes = new List<System.Type>();
        }

        /// <summary>
        /// 执行房间业务组件装配流程。
        /// componentIds 数组顺序即装配顺序，也是 RoomComponentIds 写入顺序。
        /// 任一步骤失败立即触发原子回滚，回滚完成后返回 false。
        /// 装配成功后调用 RoomInstance.MarkRunning() 与 DispatchOnRoomCreate()。
        /// 若房间挂载了 ReplayRecorder，装配成功后注入 TickGetter 与 ComponentIds。
        /// </summary>
        public bool Assemble(RoomInstance room, string[] componentIds, RoomComponentRegistry registry)
        {
            if (room == null)
            {
                Debug.LogError("[ServerRoomAssembler] Assemble 失败：room 为 null。");
                return false;
            }

            if (componentIds == null || componentIds.Length == 0)
            {
                Debug.LogWarning($"[ServerRoomAssembler] Assemble 警告：componentIds 为空，RoomId={room.RoomId}，将创建无业务组件的基础空房间。");
                room.SetComponentIds(new string[0]); // 注入空列表
                room.MarkRunning();
                room.DispatchOnRoomCreate();
                return true;
            }

            if (registry == null)
            {
                Debug.LogError($"[ServerRoomAssembler] Assemble 失败：registry 为 null，RoomId={room.RoomId}。");
                return false;
            }

            var assembleRecords = new List<AssembleRecord>();
            for (int i = 0; i < componentIds.Length; i++)
            {
                string componentId = componentIds[i];
                // 步骤 1：通过注册表创建组件实例
                var rawComponent = registry.CreateComponent(componentId, room);
                if (rawComponent == null)
                {
                    Debug.LogError($"[ServerRoomAssembler] 组件实例化失败，componentId={componentId}，RoomId={room.RoomId}，触发原子回滚。");
                    Rollback(room, assembleRecords);
                    return false;
                }

                var component = rawComponent as IInitializableRoomComponent;
                if (component == null)
                {
                    Debug.LogError($"[ServerRoomAssembler] 组件 {componentId} 未实现 IInitializableRoomComponent 接口，RoomId={room.RoomId}，触发原子回滚。");
                    Rollback(room, assembleRecords);
                    return false;
                }

                var record = new AssembleRecord { Component = component };

                // 步骤 2：初始化组件
                bool initSuccess = component.Init(room);
                if (!initSuccess)
                {
                    Debug.LogError($"[ServerRoomAssembler] 组件初始化失败，componentId={componentId}，RoomId={room.RoomId}，触发原子回滚。");
                    Rollback(room, assembleRecords);
                    return false;
                }

                record.IsInitialized = true;

                // 步骤 3：将组件声明的协议处理能力绑定到 Router
                var bindings = component.GetHandlerBindings();
                if (bindings != null)
                {
                    foreach (var binding in bindings)
                    {
                        if (binding == null || binding.MessageType == null || binding.Handler == null)
                        {
                            Debug.LogError($"[ServerRoomAssembler] 组件 {componentId} 的 HandlerBinding 存在 null 字段，RoomId={room.RoomId}，触发原子回滚。");
                            Rollback(room, assembleRecords);
                            return false;
                        }

                        bool registerSuccess = room.MessageRouter.Register(binding.MessageType, binding.Handler);
                        if (!registerSuccess)
                        {
                            Debug.LogError($"[ServerRoomAssembler] 协议类型 {binding.MessageType.Name} Router 绑定失败（可能重复注册），" +
                                           $"componentId={componentId}，RoomId={room.RoomId}，触发原子回滚。");
                            Rollback(room, assembleRecords);
                            return false;
                        }

                        record.RegisteredMessageTypes.Add(binding.MessageType);
                    }
                }

                // 步骤 4：将组件写入 RoomInstance 组件列表
                room.AddComponent(component);
                assembleRecords.Add(record);
            }

            // 注入组件 ID 清单到 RoomInstance，供重连恢复与回放使用
            room.SetComponentIds(componentIds);

            // 所有组件装配成功，推进 RoomInstance 到 Running 状态
            room.MarkRunning();

            // 若挂载了 ReplayRecorder，注入 TickGetter 与 ComponentIds
            if (room.ReplayRecorder != null)
            {
                room.ReplayRecorder.SetTickGetter(() => room.CurrentTick);
                room.ReplayRecorder.SetRoomComponentIds(componentIds);
            }

            // 分发 OnRoomCreate 回调给所有已装配组件
            room.DispatchOnRoomCreate();
            Debug.Log($"[ServerRoomAssembler] 房间装配成功，RoomId={room.RoomId}，组件数量={assembleRecords.Count}。");
            return true;
        }

        /// <summary>
        /// 原子回滚：按装配逆序注销 Router 绑定、反初始化组件、从 RoomInstance 移除组件。
        /// 任一回滚步骤失败时仍继续执行后续回滚步骤，确保最终不留脏状态。
        /// 只有在回滚完成后，才允许向上层返回装配失败结果。
        /// </summary>
        private void Rollback(RoomInstance room, List<AssembleRecord> records)
        {
            Debug.Log($"[ServerRoomAssembler] 开始原子回滚，RoomId={room.RoomId}，已装配组件数={records.Count}。");
            // 按装配逆序执行回滚
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var record = records[i];

                // 回滚步骤 1：按装配逆序注销 Router 绑定
                for (int j = record.RegisteredMessageTypes.Count - 1; j >= 0; j--)
                {
                    var msgType = record.RegisteredMessageTypes[j];
                    // 注销时传入 null handler，Router 内部通过类型键移除
                    // 此处直接调用 Router.ClearAll 不适用（会清除其他组件），改为逐类型注销
                    // 由于 Router.Unregister 需要原始 handler 引用做来源校验，此处通过 ClearAll 后重新注册其他组件
                    // 实际工程中建议 Router 提供 ForceUnregister(Type) 接口供装配器回滚使用
                    room.MessageRouter.ForceUnregisterByType(msgType);
                }

                // 回滚步骤 2：反初始化组件
                if (record.IsInitialized)
                {
                    record.Component.Deinit();
                }

                // 回滚步骤 3：从 RoomInstance 组件列表移除
                room.RemoveComponent(record.Component);
            }

            Debug.Log($"[ServerRoomAssembler] 原子回滚完成，RoomId={room.RoomId}。");
        }
    }
}
