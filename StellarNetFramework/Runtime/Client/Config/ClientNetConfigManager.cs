using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace StellarNet.Client.Config
{
    /// <summary>
    /// 客户端进程级配置管理器，负责 ClientNetConfig.json 的装载与动态项热重载。
    /// 严格归属客户端，服务端不得直接读取客户端配置文件。
    /// 热重载只刷新动态配置项，若检测到静态项变更则输出 Warning 并忽略变更。
    /// Reload() 必须由宿主层显式调用，框架不内置文件监听自动触发机制。
    /// </summary>
    public sealed class ClientNetConfigManager
    {
        /// <summary>
        /// 当前生效的配置实例，外部只读。
        /// </summary>
        public ClientNetConfig Current { get; private set; }

        private readonly string _configFilePath;

        // 首次加载时的静态配置项快照，用于热重载时的静态项变更检测
        private string _initialServerAddress;
        private int _initialServerPort;
        private string _initialFrameworkVersion;
        private string _initialProtocolVersion;

        public ClientNetConfigManager(string configFilePath)
        {
            if (string.IsNullOrEmpty(configFilePath))
            {
                Debug.LogError("[ClientNetConfigManager] 配置文件路径不能为空，将使用默认配置。");
                Current = new ClientNetConfig();
                CacheStaticSnapshot();
                return;
            }

            _configFilePath = configFilePath;
            Current = LoadFromFile(_configFilePath) ?? new ClientNetConfig();
            CacheStaticSnapshot();
            ValidateConfig(Current);
        }

        /// <summary>
        /// 热重载动态配置项，必须由宿主层显式调用。
        /// 若检测到静态项变更，输出 Warning 并忽略该项变更，保持原值不变。
        /// </summary>
        public void Reload()
        {
            if (string.IsNullOrEmpty(_configFilePath))
            {
                Debug.LogWarning("[ClientNetConfigManager] 配置文件路径为空，无法执行热重载。");
                return;
            }

            var newConfig = LoadFromFile(_configFilePath);
            if (newConfig == null)
            {
                Debug.LogError("[ClientNetConfigManager] 热重载失败：配置文件解析失败，保持当前配置不变。");
                return;
            }

            bool hasStaticChange = false;

            if (newConfig.ServerAddress != _initialServerAddress)
            {
                Debug.LogWarning($"[ClientNetConfigManager] 热重载检测到静态配置项 ServerAddress 变更，已忽略，修改需重启进程。");
                newConfig.ServerAddress = _initialServerAddress;
                hasStaticChange = true;
            }

            if (newConfig.ServerPort != _initialServerPort)
            {
                Debug.LogWarning($"[ClientNetConfigManager] 热重载检测到静态配置项 ServerPort 变更，已忽略，修改需重启进程。");
                newConfig.ServerPort = _initialServerPort;
                hasStaticChange = true;
            }

            if (newConfig.FrameworkVersion != _initialFrameworkVersion)
            {
                Debug.LogWarning($"[ClientNetConfigManager] 热重载检测到静态配置项 FrameworkVersion 变更，已忽略，修改需重启进程。");
                newConfig.FrameworkVersion = _initialFrameworkVersion;
                hasStaticChange = true;
            }

            if (newConfig.ProtocolVersion != _initialProtocolVersion)
            {
                Debug.LogWarning($"[ClientNetConfigManager] 热重载检测到静态配置项 ProtocolVersion 变更，已忽略，修改需重启进程。");
                newConfig.ProtocolVersion = _initialProtocolVersion;
                hasStaticChange = true;
            }

            ValidateConfig(newConfig);
            Current = newConfig;

            if (hasStaticChange)
            {
                Debug.Log("[ClientNetConfigManager] 热重载完成（含静态项变更警告），动态配置项已刷新。");
            }
            else
            {
                Debug.Log("[ClientNetConfigManager] 热重载完成，动态配置项已刷新。");
            }
        }

        private ClientNetConfig LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"[ClientNetConfigManager] 配置文件不存在：{path}，将使用默认配置。");
                return null;
            }

            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"[ClientNetConfigManager] 配置文件内容为空：{path}，将使用默认配置。");
                return null;
            }

            ClientNetConfig config = JsonConvert.DeserializeObject<ClientNetConfig>(json);

            if (config == null)
            {
                Debug.LogError($"[ClientNetConfigManager] 配置文件反序列化失败：{path}，将使用默认配置。");
                return null;
            }

            return config;
        }

        private void ValidateConfig(ClientNetConfig config)
        {
            if (config.ServerPort <= 0 || config.ServerPort > 65535)
            {
                Debug.LogWarning($"[ClientNetConfigManager] ServerPort 配置值非法（{config.ServerPort}），已修正为默认值 7777。");
                config.ServerPort = 7777;
            }

            if (config.ReconnectMaxAttempts < 0)
            {
                Debug.LogWarning($"[ClientNetConfigManager] ReconnectMaxAttempts 配置值非法（{config.ReconnectMaxAttempts}），已修正为默认值 3。");
                config.ReconnectMaxAttempts = 3;
            }

            if (config.ConnectTimeoutSeconds <= 0f)
            {
                Debug.LogWarning($"[ClientNetConfigManager] ConnectTimeoutSeconds 配置值非法（{config.ConnectTimeoutSeconds}），已修正为默认值 10。");
                config.ConnectTimeoutSeconds = 10f;
            }
        }

        private void CacheStaticSnapshot()
        {
            _initialServerAddress = Current.ServerAddress;
            _initialServerPort = Current.ServerPort;
            _initialFrameworkVersion = Current.FrameworkVersion;
            _initialProtocolVersion = Current.ProtocolVersion;
        }
    }
}
