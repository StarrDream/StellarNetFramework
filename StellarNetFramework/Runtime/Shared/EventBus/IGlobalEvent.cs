namespace StellarNet.Shared.EventBus
{
    /// <summary>
    /// 全局域领域事件标记接口。
    /// GlobalEventBus 只允许处理实现了此接口的事件类型，防止跨域事件误投递。
    /// 此接口与协议基类不得混合继承，网络协议类型不得直接作为 EventBus 事件类型使用。
    /// 典型场景：服务端全局模块之间的领域事件传播，例如用户上线通知、房间创建完成通知等。
    /// </summary>
    public interface IGlobalEvent
    {
    }
}