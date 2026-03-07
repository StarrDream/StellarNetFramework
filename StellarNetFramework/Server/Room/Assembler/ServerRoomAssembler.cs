// Assets/StellarNetFramework/Server/Room/Assembler/ServerRoomAssembler.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Server.Network.Sender;
using StellarNet.Server.Room.Component;

namespace StellarNet.Server.Room.Assembler
{
    // 服务端房间装配器，负责将业务组件列表原子性装配到指定 RoomInstance。
    // 装配流程：按顺序逐个调用组件 Init()，任意一个组件 Init() 返回 false 时，
    // 立即触发原子回滚：按逆序调用已成功初始化组件的 OnDestroy()，
    // 然后调用 RoomInstance.Destroy() 完成整体销毁，防止半初始化房间进入 Running 状态。
    // 装配器本身不持有任何房间运行时状态，所有方法均为静态，不允许实例化。
    // ComponentId 唯一性校验在装配阶段完成，重复 ComponentId 直接阻断装配并触发回滚。
    // 框架层对象（sender、roomInstance）由装配器从外部取得后直接注入每个组件，
    // 组件不允许通过任何寻址机制自行获取框架层对象。
    public static class ServerRoomAssembler
    {
        // 将组件列表原子性装配到指定 RoomInstance。
        // 参数 room：目标房间实例，必须处于 Initializing 状态。
        // 参数 components：待装配的组件列表，按顺序装配，不得为 null 或空。
        // 参数 sender：房间域消息发送器，由 GlobalInfrastructure 持有并传入，直接注入每个组件。
        // 参数 replayRecorderWriter：可选的 ReplayRecorder 写入接口，为 null 时跳过录制挂载。
        // 返回 true 表示装配成功，RoomInstance 已进入 Running 状态；
        // 返回 false 表示装配失败，RoomInstance 已完成原子回滚并进入 Destroyed 状态。
        public static bool Assemble(
            RoomInstance room,
            IReadOnlyList<IServerRoomComponent> components,
            ServerRoomMessageSender sender,
            IReplayRecorderWriter replayRecorderWriter = null)
        {
            if (room == null)
            {
                Debug.LogError("[ServerRoomAssembler] Assemble 失败：room 不得为 null");
                return false;
            }

            if (room.LifecycleState != RoomLifecycleState.Initializing)
            {
                Debug.LogError(
                    $"[ServerRoomAssembler] Assemble 失败：RoomId={room.RoomId} 当前状态为 " +
                    $"{room.LifecycleState}，装配器只允许对 Initializing 状态的房间执行装配。");
                return false;
            }

            if (components == null || components.Count == 0)
            {
                Debug.LogError(
                    $"[ServerRoomAssembler] Assemble 失败：组件列表为 null 或空，RoomId={room.RoomId}，" +
                    $"至少需要一个业务组件才能完成房间装配。");
                room.Destroy();
                return false;
            }

            if (sender == null)
            {
                Debug.LogError(
                    $"[ServerRoomAssembler] Assemble 失败：sender 不得为 null，RoomId={room.RoomId}");
                room.Destroy();
                return false;
            }

            // 挂载 ReplayRecorder（可选）
            if (replayRecorderWriter != null)
                room.SetReplayRecorder(replayRecorderWriter);

            // ComponentId 唯一性预校验，在 Init 之前完成，避免部分 Init 后发现冲突再回滚
            var componentIdSet = new HashSet<string>();
            foreach (var component in components)
            {
                if (component == null)
                {
                    Debug.LogError(
                        $"[ServerRoomAssembler] Assemble 失败：组件列表中存在 null 元素，" +
                        $"RoomId={room.RoomId}，装配已阻断。");
                    room.Destroy();
                    return false;
                }

                if (string.IsNullOrEmpty(component.ComponentId))
                {
                    Debug.LogError(
                        $"[ServerRoomAssembler] Assemble 失败：组件 {component.GetType().Name} " +
                        $"的 ComponentId 为空，RoomId={room.RoomId}，装配已阻断。");
                    room.Destroy();
                    return false;
                }

                if (componentIdSet.Contains(component.ComponentId))
                {
                    Debug.LogError(
                        $"[ServerRoomAssembler] Assemble 失败：发现重复 ComponentId={component.ComponentId}，" +
                        $"RoomId={room.RoomId}，同一 RoomInstance 内组件 ID 必须唯一，装配已阻断。");
                    room.Destroy();
                    return false;
                }

                componentIdSet.Add(component.ComponentId);
            }

            // 记录已成功初始化的组件，用于失败时原子回滚
            var initializedComponents = new List<IServerRoomComponent>();

            foreach (var component in components)
            {
                // 框架层对象由装配器直接注入，组件不做任何寻址
                var success = component.Init(
                    room.Router,
                    room.ServiceLocator,
                    room.EventBus,
                    sender,
                    room,
                    room.RoomId);

                if (!success)
                {
                    Debug.LogError(
                        $"[ServerRoomAssembler] 组件 {component.ComponentId}（{component.GetType().Name}）" +
                        $"Init() 返回 false，RoomId={room.RoomId}，" +
                        $"触发原子回滚，已成功初始化的组件数量：{initializedComponents.Count}");

                    // 原子回滚：按逆序调用已成功初始化组件的 OnDestroy()
                    RollbackComponents(initializedComponents, room.RoomId);

                    // 整体销毁房间，防止半初始化房间进入 Running 状态
                    room.Destroy();
                    return false;
                }

                // Init 成功，注册到 RoomInstance 组件列表
                room.AddComponent(component);
                initializedComponents.Add(component);
            }

            // 全部组件装配成功，标记房间进入 Running 状态
            room.MarkRunning();
            return true;
        }

        // 原子回滚：按逆序调用已成功初始化组件的 OnDestroy()。
        // 回滚过程中单个组件 OnDestroy() 抛出异常时，记录错误日志并继续回滚其余组件，
        // 不允许单个组件回滚失败阻断整体回滚流程，防止产生更多脏数据。
        private static void RollbackComponents(
            List<IServerRoomComponent> initializedComponents,
            string roomId)
        {
            for (var i = initializedComponents.Count - 1; i >= 0; i--)
            {
                var component = initializedComponents[i];

                if (component == null)
                    continue;

                // 回滚阶段允许捕获单个组件异常，防止单点失败阻断整体回滚。
                // 此处是框架级回滚保护，不属于常规业务逻辑，符合 try-catch 使用约束。
                bool rollbackFailed = false;
                Exception rollbackException = null;

                try
                {
                    component.OnDestroy();
                }
                catch (Exception e)
                {
                    rollbackFailed = true;
                    rollbackException = e;
                }

                if (rollbackFailed)
                {
                    Debug.LogError(
                        $"[ServerRoomAssembler] 回滚阶段组件 {component.ComponentId} OnDestroy() 抛出异常，" +
                        $"RoomId={roomId}，异常信息：{rollbackException?.Message}，" +
                        $"继续回滚其余组件，防止产生更多脏数据。");
                }
            }
        }
    }
}