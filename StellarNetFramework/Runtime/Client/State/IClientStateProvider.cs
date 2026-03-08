namespace StellarNet.Client.State
{
    /// <summary>
    /// 客户端状态提供者接口。
    /// 用于向底层协议过滤器等基础设施提供当前客户端主状态，解耦对 GlobalClientManager 的反向强依赖。
    /// </summary>
    public interface IClientStateProvider
    {
        /// <summary>
        /// 获取当前客户端主状态。
        /// </summary>
        ClientAppState CurrentState { get; }
    }
}