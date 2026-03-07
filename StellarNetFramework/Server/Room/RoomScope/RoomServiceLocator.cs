// Assets/StellarNetFramework/Server/Room/RoomScope/RoomServiceLocator.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Server.Infrastructure.GlobalScope;

namespace StellarNet.Server.Room.RoomScope
{
    // 服务端房间作用域服务定位器。
    // 底层结构采用 Dictionary<Type, IRoomService>，支持接口类型寻址。
    // 每个 RoomInstance 持有自己独立的 RoomServiceLocator 实例，作用域严格绑定到单个房间生命周期。
    // 跨域调用（尝试注册 IGlobalService 到此容器）直接报错阻断。
    // 不是通用 IoC 容器，不是状态仓库，不是跨房间访问入口，不是对象池。
    public sealed class RoomServiceLocator
    {
        // 所属房间 ID，用于错误日志定位
        private readonly string _roomId;

        // 以注册时传入的接口类型为 Key，保证 O(1) 查找
        private readonly Dictionary<Type, IRoomService> _services = new Dictionary<Type, IRoomService>();

        public RoomServiceLocator(string roomId)
        {
            _roomId = roomId ?? string.Empty;
        }

        // 注册房间服务。
        // 参数 interfaceType：必须是 IRoomService 的子接口或实现类型，作为寻址 Key。
        // 参数 service：具体实现实例，不得为 null。
        // 若 interfaceType 已存在注册，直接报错阻断，不允许静默覆盖。
        public void Register(Type interfaceType, IRoomService service)
        {
            if (interfaceType == null)
            {
                Debug.LogError($"[RoomServiceLocator] RoomId={_roomId} 注册失败：interfaceType 不得为 null");
                return;
            }

            if (service == null)
            {
                Debug.LogError(
                    $"[RoomServiceLocator] RoomId={_roomId} 注册失败：服务实例不得为 null，" +
                    $"注册类型：{interfaceType.Name}");
                return;
            }

            // 跨域保护：IGlobalService 不得注册到房间作用域
            if (typeof(IGlobalService).IsAssignableFrom(interfaceType))
            {
                Debug.LogError(
                    $"[RoomServiceLocator] RoomId={_roomId} 跨域误注册阻断：" +
                    $"类型 {interfaceType.Name} 实现了 IGlobalService，" +
                    $"不允许注册到 RoomServiceLocator，请使用 GlobalServiceLocator。");
                return;
            }

            if (_services.ContainsKey(interfaceType))
            {
                Debug.LogError(
                    $"[RoomServiceLocator] RoomId={_roomId} 注册冲突：类型 {interfaceType.Name} 已存在注册，" +
                    $"同一 Scope 内不允许同类型多实例，当前操作已阻断。");
                return;
            }

            _services[interfaceType] = service;
        }

        // 泛型注册重载，以 TInterface 作为寻址 Key
        public void Register<TInterface>(TInterface service) where TInterface : class, IRoomService
        {
            Register(typeof(TInterface), service);
        }

        // 通过接口类型获取房间服务。
        // 查找失败返回 null，由调用方决定是否阻断后续逻辑。
        public TInterface Get<TInterface>() where TInterface : class, IRoomService
        {
            if (_services.TryGetValue(typeof(TInterface), out var service))
                return service as TInterface;

            return null;
        }

        // 非泛型获取重载，用于运行时动态类型查找场景
        public IRoomService Get(Type interfaceType)
        {
            if (interfaceType == null)
                return null;

            _services.TryGetValue(interfaceType, out var service);
            return service;
        }

        // 注销指定类型的房间服务，用于房间销毁时按逆序反初始化
        public void Unregister<TInterface>() where TInterface : class, IRoomService
        {
            Unregister(typeof(TInterface));
        }

        public void Unregister(Type interfaceType)
        {
            if (interfaceType == null)
            {
                Debug.LogError($"[RoomServiceLocator] RoomId={_roomId} 注销失败：interfaceType 不得为 null");
                return;
            }

            if (!_services.ContainsKey(interfaceType))
            {
                Debug.LogWarning(
                    $"[RoomServiceLocator] RoomId={_roomId} 注销警告：类型 {interfaceType.Name} 未在当前 Scope 中注册，" +
                    $"注销操作已忽略。");
                return;
            }

            _services.Remove(interfaceType);
        }

        // 清空全部注册，用于房间销毁阶段兜底清理
        public void Clear()
        {
            _services.Clear();
        }

        // 当前已注册服务数量，用于诊断与自检
        public int Count => _services.Count;
    }
}
