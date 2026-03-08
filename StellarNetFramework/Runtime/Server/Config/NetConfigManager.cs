using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace StellarNet.Server.Config
{
    /// <summary>
    /// 服务端进程级配置管理器，负责 NetConfig.json 的装载与动态项热重载。
    /// 严格归属服务端，客户端不得直接读取服务端配置文件。
    /// 热重载只刷新动态配置项，若检测到静态项变更则输出 Warning 并忽略变更。
    /// Reload() 必须由宿主层显式调用，框架不内置文件监听自动触发机制。
    /// 动态配置刷新后只对后续新创建对象生效，不追溯改变已在运行中的房间实例。
    /// </summary>
    public sealed class NetConfigManager
    {
        /// <summary>
        /// 当前生效的配置实例，外部只读。
        /// </summary>
        public NetConfig Current { get; private set; }

        private readonly string _configFilePath;

        // 保存首次加载时的静态配置项快照，用于热重载时的静态项变更检测
        private string _initialListenAddress;
        private int _initialListenPort;
        private int _initialMaxConnections;
        private string _initialFrameworkVersion;
        private string _initialProtocolVersion;

        public NetConfigManager(string configFilePath)
        {
            if (string.IsNullOrEmpty(configFilePath))
            {
                Debug.LogError("[NetConfigManager] 配置文件路径不能为空，将使用默认配置。");
                Current = new NetConfig();
                CacheStaticSnapshot();
                return;
            }

            _configFilePath = configFilePath;
            Current = LoadFromFile(_configFilePath) ?? new NetConfig();
            CacheStaticSnapshot();
            ValidateConfig(Current);
        }

        /// <summary>
        /// 热重载动态配置项。
        /// 必须由宿主层显式调用，框架不内置文件监听自动触发机制。
        /// 若检测到静态项变更，输出 Warning 并忽略该项变更，保持原值不变。
        /// 动态配置刷新后只对后续新创建对象生效，不追溯改变已在运行中的房间实例。
        /// </summary>
        public void Reload()
        {
            if (string.IsNullOrEmpty(_configFilePath))
            {
                Debug.LogWarning("[NetConfigManager] 配置文件路径为空，无法执行热重载。");
                return;
            }

            var newConfig = LoadFromFile(_configFilePath);
            if (newConfig == null)
            {
                Debug.LogError("[NetConfigManager] 热重载失败：配置文件解析失败，保持当前配置不变。");
                return;
            }

            // 检测静态配置项是否发生变更，发现变更则输出 Warning 并还原为初始值
            bool hasStaticChange = false;

            if (newConfig.ListenAddress != _initialListenAddress)
            {
                Debug.LogWarning($"[NetConfigManager] 热重载检测到静态配置项 ListenAddress 变更（{_initialListenAddress} → {newConfig.ListenAddress}），已忽略，修改需重启进程。");
                newConfig.ListenAddress = _initialListenAddress;
                hasStaticChange = true;
            }

            if (newConfig.ListenPort != _initialListenPort)
            {
                Debug.LogWarning($"[NetConfigManager] 热重载检测到静态配置项 ListenPort 变更（{_initialListenPort} → {newConfig.ListenPort}），已忽略，修改需重启进程。");
                newConfig.ListenPort = _initialListenPort;
                hasStaticChange = true;
            }

            if (newConfig.MaxConnections != _initialMaxConnections)
            {
                Debug.LogWarning($"[NetConfigManager] 热重载检测到静态配置项 MaxConnections 变更（{_initialMaxConnections} → {newConfig.MaxConnections}），已忽略，修改需重启进程。");
                newConfig.MaxConnections = _initialMaxConnections;
                hasStaticChange = true;
            }

            if (newConfig.FrameworkVersion != _initialFrameworkVersion)
            {
                Debug.LogWarning($"[NetConfigManager] 热重载检测到静态配置项 FrameworkVersion 变更，已忽略，修改需重启进程。");
                newConfig.FrameworkVersion = _initialFrameworkVersion;
                hasStaticChange = true;
            }

            if (newConfig.ProtocolVersion != _initialProtocolVersion)
            {
                Debug.LogWarning($"[NetConfigManager] 热重载检测到静态配置项 ProtocolVersion 变更，已忽略，修改需重启进程。");
                newConfig.ProtocolVersion = _initialProtocolVersion;
                hasStaticChange = true;
            }

            ValidateConfig(newConfig);
            Current = newConfig;

            if (hasStaticChange)
            {
                Debug.Log("[NetConfigManager] 热重载完成（含静态项变更警告），动态配置项已刷新，对后续新创建对象生效。");
            }
            else
            {
                Debug.Log("[NetConfigManager] 热重载完成，动态配置项已刷新，对后续新创建对象生效。");
            }
        }

        /// <summary>
        /// 从文件路径加载并解析配置，失败返回 null。
        /// </summary>
        private NetConfig LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"[NetConfigManager] 配置文件不存在：{path}，将使用默认配置。");
                return null;
            }

            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"[NetConfigManager] 配置文件内容为空：{path}，将使用默认配置。");
                return null;
            }

            NetConfig config = JsonConvert.DeserializeObject<NetConfig>(json);

            if (config == null)
            {
                Debug.LogError($"[NetConfigManager] 配置文件反序列化失败：{path}，将使用默认配置。");
                return null;
            }

            return config;
        }

        /// <summary>
        /// 校验配置项合法性，发现非法值输出 Warning 并修正为安全默认值。
        /// 重点校验幂等清理间隔与 TTL 的关系，防止清理逻辑失效。
        /// </summary>
        private void ValidateConfig(NetConfig config)
        {
            if (config.IdempotentCleanupIntervalSeconds >= config.IdempotentTtlSeconds)
            {
                Debug.LogWarning($"[NetConfigManager] 配置校验警告：IdempotentCleanupIntervalSeconds({config.IdempotentCleanupIntervalSeconds}s) >= IdempotentTtlSeconds({config.IdempotentTtlSeconds}s)，" +
                                 "后台巡检间隔不应大于等于 TTL，可能导致过期缓存无法及时清理。");
            }

            if (config.ReplayBufferCapacity <= 0)
            {
                Debug.LogWarning($"[NetConfigManager] ReplayBufferCapacity 配置值非法（{config.ReplayBufferCapacity}），已修正为默认值 1024。");
                config.ReplayBufferCapacity = 1024;
            }

            if (config.MaxConnections <= 0)
            {
                Debug.LogWarning($"[NetConfigManager] MaxConnections 配置值非法（{config.MaxConnections}），已修正为默认值 200。");
                config.MaxConnections = 200;
            }

            if (config.ListenPort <= 0 || config.ListenPort > 65535)
            {
                Debug.LogWarning($"[NetConfigManager] ListenPort 配置值非法（{config.ListenPort}），已修正为默认值 7777。");
                config.ListenPort = 7777;
            }
        }

        /// <summary>
        /// 缓存首次加载时的静态配置项快照，用于热重载时的静态项变更检测。
        /// </summary>
        private void CacheStaticSnapshot()
        {
            _initialListenAddress = Current.ListenAddress;
            _initialListenPort = Current.ListenPort;
            _initialMaxConnections = Current.MaxConnections;
            _initialFrameworkVersion = Current.FrameworkVersion;
            _initialProtocolVersion = Current.ProtocolVersion;
        }
    }
}
