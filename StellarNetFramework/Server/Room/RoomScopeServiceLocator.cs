using StellarNet.Server.ServiceLocator;
using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.Room
{
    /// <summary>
    /// 房间作用域服务定位器，只用于单个房间作用域内服务寻址。
    /// 每个 RoomInstance 持有自己独立的 RoomScopeServiceLocator 实例。
    /// 只允许注册实现了 IRoomService 标记接口的服务类型，防止跨域误注册。
    /// 不能替代明确的业务契约设计，不是状态仓库。
    /// </summary>
    public sealed class RoomScopeServiceLocator
    {
        private readonly ScopeServiceLocator _inner;
        private readonly string _roomId;

        public RoomScopeServiceLocator(string roomId)
        {
            _roomId = roomId ?? string.Empty;
            _inner = new ScopeServiceLocator($"RoomScope({_roomId})");
        }

        /// <summary>
        /// 注册房间域服务。
        /// 只允许注册实现了 IRoomService 的类型，跨域误注册时直接报错阻断。
        /// </summary>
        public void Register<TService>(TService service)
            where TService : class, IRoomService
        {
            if (service == null)
            {
                Debug.LogError($"[RoomScopeServiceLocator] Register 失败：service 为 null，类型={typeof(TService).Name}，RoomId={_roomId}。");
                return;
            }
            _inner.Register(service);
        }

        /// <summary>
        /// 获取房间域服务实例。
        /// 只允许获取实现了 IRoomService 的类型，防止误获取全局域服务。
        /// </summary>
        public TService Get<TService>()
            where TService : class, IRoomService
        {
            return _inner.Get<TService>();
        }

        /// <summary>
        /// 注销房间域服务。
        /// </summary>
        public void Unregister<TService>()
            where TService : class, IRoomService
        {
            _inner.Unregister<TService>();
        }

        /// <summary>
        /// 清空所有房间域服务，在 RoomInstance.Destroy() 阶段调用。
        /// </summary>
        public void Clear()
        {
            _inner.Clear();
        }
    }
}