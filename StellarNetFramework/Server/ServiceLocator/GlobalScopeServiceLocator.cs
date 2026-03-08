using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.ServiceLocator
{
    /// <summary>
    /// 全局作用域服务定位器，只用于全局作用域内服务寻址。
    /// 只允许注册实现了 IGlobalService 标记接口的服务类型，防止跨域误注册。
    /// 由 GlobalInfrastructure 持有，生命周期与 GlobalInfrastructure 一致。
    /// 不是状态仓库，不是跨域访问入口。
    /// </summary>
    public sealed class GlobalScopeServiceLocator
    {
        private readonly ScopeServiceLocator _inner;

        public GlobalScopeServiceLocator()
        {
            _inner = new ScopeServiceLocator("GlobalScope");
        }

        /// <summary>
        /// 注册全局域服务。
        /// 只允许注册实现了 IGlobalService 的类型，跨域误注册时直接报错阻断。
        /// </summary>
        public void Register<TService>(TService service)
            where TService : class, IGlobalService
        {
            if (service == null)
            {
                Debug.LogError($"[GlobalScopeServiceLocator] Register 失败：service 为 null，类型={typeof(TService).Name}。");
                return;
            }
            _inner.Register(service);
        }

        /// <summary>
        /// 获取全局域服务实例。
        /// 只允许获取实现了 IGlobalService 的类型，防止误获取房间域服务。
        /// </summary>
        public TService Get<TService>()
            where TService : class, IGlobalService
        {
            return _inner.Get<TService>();
        }

        /// <summary>
        /// 注销全局域服务。
        /// </summary>
        public void Unregister<TService>()
            where TService : class, IGlobalService
        {
            _inner.Unregister<TService>();
        }

        /// <summary>
        /// 清空所有全局域服务，在 GlobalInfrastructure.Shutdown() 阶段调用。
        /// </summary>
        public void Clear()
        {
            _inner.Clear();
        }
    }
}