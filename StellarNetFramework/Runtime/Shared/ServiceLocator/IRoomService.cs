namespace StellarNet.Shared.ServiceLocator
{
    /// <summary>
    /// 房间域服务标记接口，用于强化 RoomScope ServiceLocator 的注册边界与作用域归属。
    /// 只有实现了此接口的服务类型才允许注册到 RoomScope ServiceLocator。
    /// 跨作用域误注册时，ServiceLocator 必须直接报错并阻断。
    /// </summary>
    public interface IRoomService
    {
    }
}