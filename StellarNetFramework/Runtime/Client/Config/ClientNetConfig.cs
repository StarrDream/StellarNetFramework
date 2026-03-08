namespace StellarNet.Client.Config
{
    /// <summary>
    /// 客户端进程级配置模型，对应 ClientNetConfig.json 文件结构。
    /// 严格归属客户端，服务端不得直接读取此配置。
    /// 静态配置项：仅启动时读取，修改必须重启进程。
    /// 动态配置项：支持热重载，刷新后只对后续新创建对象生效。
    /// </summary>
    [System.Serializable]
    public sealed class ClientNetConfig
    {
        // ── 静态配置项（修改需重启进程）──────────────────────────────

        /// <summary>
        /// 服务端连接地址，静态配置项。
        /// </summary>
        public string ServerAddress = "127.0.0.1";

        /// <summary>
        /// 服务端连接端口，静态配置项。
        /// </summary>
        public int ServerPort = 7777;

        /// <summary>
        /// 框架版本标识，用于与服务端版本一致性校验，静态配置项。
        /// </summary>
        public string FrameworkVersion = "1.0.0";

        /// <summary>
        /// 协议版本标识，用于与服务端协议一致性校验，静态配置项。
        /// </summary>
        public string ProtocolVersion = "1.0.0";

        // ── 动态配置项（支持热重载，修改后对后续新创建对象生效）──────

        /// <summary>
        /// 连接超时时长（秒），动态配置项。
        /// 超出此时间未完成连接则视为连接失败。
        /// </summary>
        public float ConnectTimeoutSeconds = 10f;

        /// <summary>
        /// 重连最大尝试次数，动态配置项。
        /// 达到上限后停止重连并通知上层业务。
        /// </summary>
        public int ReconnectMaxAttempts = 3;

        /// <summary>
        /// 重连尝试间隔（秒），动态配置项。
        /// </summary>
        public float ReconnectIntervalSeconds = 3f;

        /// <summary>
        /// 回放文件下载超时时长（秒），动态配置项。
        /// </summary>
        public float ReplayDownloadTimeoutSeconds = 30f;

        /// <summary>
        /// 回放分块请求超时时长（秒），动态配置项。
        /// 单次分块请求超过此时间未收到响应则视为超时，触发重试。
        /// </summary>
        public float ReplayChunkTimeoutSeconds = 5f;

        /// <summary>
        /// 回放分块请求最大重试次数，动态配置项。
        /// </summary>
        public int ReplayChunkMaxRetries = 3;
    }
}