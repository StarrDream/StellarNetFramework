// Assets/StellarNetFramework/Server/Infrastructure/GlobalScope/GlobalServiceLocator.cs

using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Server.Infrastructure.GlobalScope
{
    // 服务端全局作用域服务定位器。
    // 底层结构采用 Dictionary<Type, IGlobalService>，支持接口类型寻址。
    // 注册时以接口类型为 Key，获取时同样以接口类型查找，实现解耦寻址。
    // 同一 Scope 内不允许同类型多实例存在，重复注册直接报错阻断。
    // 跨域调用（尝试注册 IRoomService 到此容器）直接报错阻断。
    // 不是通用 IoC 容器，不是状态仓库，不是跨房间访问入口，不是对象池。
    public sealed class GlobalServiceLocator
    {
        // 以注册时传入的接口类型为 Key，保证 O(1) 查找
        private readonly Dictionary<Type, IGlobalService> _services = new Dictionary<Type, IGlobalService>();

        // 注册全局服务。
        // 参数 interfaceType：必须是 IGlobalService 的子接口或实现类型，作为寻址 Key。
        // 参数 service：具体实现实例，不得为 null。
        // 若 interfaceType 已存在注册，直接报错阻断，不允许静默覆盖。
        public void Register(Type interfaceType, IGlobalService service)
        {
            if (interfaceType == null)
            {
                Debug.LogError("[GlobalServiceLocator] 注册失败：interfaceType 不得为 null");
                return;
            }

            if (service == null)
            {
                Debug.LogError($"[GlobalServiceLocator] 注册失败：服务实例不得为 null，注册类型：{interfaceType.Name}");
                return;
            }

            // 跨域保护：IRoomService 不得注册到全局作用域
            if (typeof(IRoomService).IsAssignableFrom(interfaceType))
            {
                Debug.LogError(
                    $"[GlobalServiceLocator] 跨域误注册阻断：类型 {interfaceType.Name} 实现了 IRoomService，" +
                    $"不允许注册到 GlobalServiceLocator，请使用 RoomServiceLocator。");
                return;
            }

            if (_services.ContainsKey(interfaceType))
            {
                Debug.LogError(
                    $"[GlobalServiceLocator] 注册冲突：类型 {interfaceType.Name} 已存在注册，" +
                    $"同一 Scope 内不允许同类型多实例，当前操作已阻断。");
                return;
            }

            _services[interfaceType] = service;
        }

        // 泛型注册重载，以 TInterface 作为寻址 Key
        public void Register<TInterface>(TInterface service) where TInterface : class, IGlobalService
        {
            Register(typeof(TInterface), service);
        }

        // 通过接口类型获取全局服务。
        // 查找失败返回 null，由调用方决定是否阻断后续逻辑。
        public TInterface Get<TInterface>() where TInterface : class, IGlobalService
        {
            if (_services.TryGetValue(typeof(TInterface), out var service))
                return service as TInterface;

            return null;
        }

        // 非泛型获取重载，用于运行时动态类型查找场景
        public IGlobalService Get(Type interfaceType)
        {
            if (interfaceType == null)
                return null;

            _services.TryGetValue(interfaceType, out var service);
            return service;
        }

        // 注销指定类型的全局服务，用于 GlobalInfrastructure 关停时按逆序反初始化
        public void Unregister<TInterface>() where TInterface : class, IGlobalService
        {
            Unregister(typeof(TInterface));
        }

        public void Unregister(Type interfaceType)
        {
            if (interfaceType == null)
            {
                Debug.LogError("[GlobalServiceLocator] 注销失败：interfaceType 不得为 null");
                return;
            }

            if (!_services.ContainsKey(interfaceType))
            {
                Debug.LogWarning(
                    $"[GlobalServiceLocator] 注销警告：类型 {interfaceType.Name} 未在当前 Scope 中注册，" +
                    $"注销操作已忽略。");
                return;
            }

            _services.Remove(interfaceType);
        }

        // 清空全部注册，用于关停阶段兜底清理
        public void Clear()
        {
            _services.Clear();
        }

        // 当前已注册服务数量，用于诊断与自检
        public int Count => _services.Count;
    }

    // 全局服务标记接口，所有注册到 GlobalServiceLocator 的服务必须实现此接口。
    // 用于强化注册边界与作用域归属，防止跨域误注册。
    public interface IGlobalService
    {
    }

    // 房间服务标记接口，声明在此处便于 GlobalServiceLocator 执行跨域保护检测。
    // 所有注册到 RoomServiceLocator 的服务必须实现此接口。
    public interface IRoomService
    {
    }
}
