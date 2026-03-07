// Assets/StellarNetFramework/Server/Config/NetConfig.cs

namespace StellarNet.Server.Config
{
    // 服务端网络运行配置数据模型，对应 NetConfig.json 配置文件结构。
    // 不等价于 IRoomSettings（房间业务配置快照），两者职责完全独立。
    // NetConfig 是服务端进程级运行参数，IRoomSettings 是单局房间级业务配置。
    // 所有字段均提供合理默认值，NetConfigManager 在加载失败时使用默认值继续运行。
    [System.Serializable]
    public sealed class NetConfig
    {
        // 服务端监听端口
        public int ListenPort = 7777;

        // 最大同时在线连接数
        public int MaxConnections = 200;

        // Session 保留超时时长（毫秒），断线后超过此时长未重连则销毁会话
        public long SessionRetainTimeoutMs = 30000;

        // 空置房间超时时长（毫秒），无在线成员超过此时长则销毁房间
        public long EmptyRoomTimeoutMs = 60000;

        // 单帧 EventBus 派发 Warning 阈值
        public int EventBusWarningThreshold = 100;

        // 幂等缓存最大容量（条目数）
        public int IdempotentCacheMaxSize = 10000;

        // 幂等缓存条目过期时长（毫秒）
        public long IdempotentCacheExpireMs = 300000;

        // 是否启用回放录制（全局开关，具体房间是否录制由业务层决定）
        public bool EnableReplayRecording = false;

        // 回放帧缓冲 Flush 间隔（毫秒）
        public long ReplayFlushIntervalMs = 5000;
    }
}