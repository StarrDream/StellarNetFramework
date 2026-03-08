namespace StellarNet.Server.Config
{
    /// <summary>
    /// 服务端进程级配置模型，对应 NetConfig.json 文件结构。
    /// 严格归属服务端，不属于 Shared 层，客户端不得直接读取此配置。
    /// 静态配置项：仅启动时读取，修改必须重启进程。
    /// 动态配置项：支持通过 NetConfigManager.Reload() 热重载，刷新后只对后续新创建对象生效。
    /// </summary>
    [System.Serializable]
    public sealed class NetConfig
    {
        // ── 静态配置项（修改需重启进程）──────────────────────────────

        /// <summary>
        /// 服务端监听 IP 地址，静态配置项。
        /// </summary>
        public string ListenAddress = "0.0.0.0";

        /// <summary>
        /// 服务端监听端口，静态配置项。
        /// </summary>
        public int ListenPort = 7777;

        /// <summary>
        /// 最大客户端连接数上限，静态配置项。
        /// 框架定位为 200 个客户端连接规模，超出此值的连接请求将被拒绝。
        /// </summary>
        public int MaxConnections = 200;

        /// <summary>
        /// 框架版本标识，写入回放文件头用于兼容性校验，静态配置项。
        /// </summary>
        public string FrameworkVersion = "1.0.0";

        /// <summary>
        /// 协议版本标识，写入回放文件头用于协议兼容性校验，静态配置项。
        /// </summary>
        public string ProtocolVersion = "1.0.0";

        // ── 动态配置项（支持热重载，修改后对后续新创建对象生效）──────

        /// <summary>
        /// 大厅重连超时时长（秒），动态配置项。
        /// Session 在此时间内未重连则视为过期。
        /// </summary>
        public float ReconnectTimeoutSeconds = 60f;

        /// <summary>
        /// 房间空置销毁超时时长（秒），动态配置项。
        /// 房间内所有连接均断线且持续超过此时间后，GlobalRoomManager 强制销毁该房间。
        /// 与 Session 保留超时独立计时，独立生效。
        /// </summary>
        public float RoomEmptyTimeoutSeconds = 30f;

        /// <summary>
        /// Session 保留超时时长（秒），动态配置项。
        /// 与 Room 空置销毁超时独立配置、独立计时、独立生效。
        /// </summary>
        public float SessionRetainTimeoutSeconds = 120f;

        /// <summary>
        /// 回放录制内存缓冲区容量（帧数），动态配置项。
        /// 超出时采用丢弃最旧帧 + 输出 Warning 的背压策略，绝不阻塞主线程。
        /// </summary>
        public int ReplayBufferCapacity = 1024;

        /// <summary>
        /// 回放文件下载超时时长（秒），动态配置项。
        /// </summary>
        public float ReplayDownloadTimeoutSeconds = 30f;

        /// <summary>
        /// 幂等缓存 TTL（秒），动态配置项。
        /// 幂等 Token 在此时间内有效，过期后同一 Token 视为新请求。
        /// </summary>
        public float IdempotentTtlSeconds = 30f;

        /// <summary>
        /// 幂等缓存后台巡检清理间隔（秒），动态配置项。
        /// 必须小于 IdempotentTtlSeconds，否则框架输出 Warning。
        /// </summary>
        public float IdempotentCleanupIntervalSeconds = 10f;
    }
}
