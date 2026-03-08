using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Server.ServiceLocator
{
    /// <summary>
    /// 作用域服务定位器底层泛型实现，GlobalScope 与 RoomScope 共用此底层逻辑。
    /// 对外通过 GlobalScopeServiceLocator 与 RoomScopeServiceLocator 暴露明确的作用域语义边界。
    /// 不允许将其退化为无作用域区分的大一统 Locator。
    /// 跨作用域误注册、误获取时，上层包装类负责报错并阻断。
    /// </summary>
    public sealed class ScopeServiceLocator
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        // 作用域名称，用于日志诊断
        private readonly string _scopeName;

        public ScopeServiceLocator(string scopeName)
        {
            _scopeName = scopeName ?? "Unknown";
        }

        /// <summary>
        /// 注册服务实例。
        /// 同一类型重复注册时输出 Warning 并覆盖，允许热替换但需谨慎使用。
        /// </summary>
        public void Register<TService>(TService service) where TService : class
        {
            if (service == null)
            {
                Debug.LogError($"[ScopeServiceLocator({_scopeName})] Register 失败：注册的 service 实例为 null，类型={typeof(TService).Name}。");
                return;
            }

            if (_services.ContainsKey(typeof(TService)))
            {
                Debug.LogWarning($"[ScopeServiceLocator({_scopeName})] 类型 {typeof(TService).Name} 已存在注册，将覆盖原有实例。");
            }

            _services[typeof(TService)] = service;
        }

        /// <summary>
        /// 获取已注册的服务实例。
        /// 获取失败返回 null，调用方必须做判空处理。
        /// </summary>
        public TService Get<TService>() where TService : class
        {
            if (!_services.TryGetValue(typeof(TService), out var service))
            {
                Debug.LogError($"[ScopeServiceLocator({_scopeName})] Get 失败：类型 {typeof(TService).Name} 未注册，请检查装配顺序。");
                return null;
            }
            return service as TService;
        }

        /// <summary>
        /// 注销指定类型的服务实例。
        /// </summary>
        public void Unregister<TService>() where TService : class
        {
            _services.Remove(typeof(TService));
        }

        /// <summary>
        /// 清空所有注册的服务，在作用域销毁时调用。
        /// </summary>
        public void Clear()
        {
            _services.Clear();
        }
    }
}
