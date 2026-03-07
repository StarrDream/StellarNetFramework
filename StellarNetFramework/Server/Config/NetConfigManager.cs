// Assets/StellarNetFramework/Server/Config/NetConfigManager.cs

using UnityEngine;
using StellarNet.Server.Infrastructure.GlobalScope;

namespace StellarNet.Server.Config
{
    // 服务端网络配置管理器，负责 NetConfig.json 的加载、热重载与配置分发。
    // 配置加载失败时使用默认值继续运行，不阻断服务端启动。
    // 热重载时通过回调通知各依赖模块更新配置，不要求重启服务端进程。
    // 配置文件路径由业务层在初始化时指定，框架不硬编码路径。
    public sealed class NetConfigManager : IGlobalService
    {
        // 当前生效的配置快照
        public NetConfig Current { get; private set; }

        // 配置变更通知回调，由 GlobalInfrastructure 在装配阶段注册各模块的更新方法
        private System.Action<NetConfig> _onConfigReloaded;

        public NetConfigManager()
        {
            // 初始化时使用默认配置，确保服务端在配置文件缺失时仍可正常启动
            Current = new NetConfig();
        }

        // 从 JSON 字符串加载配置
        // 参数 json：配置文件内容，由业务层负责文件读取后传入
        // 加载失败时保留当前配置并输出 Warning
        public void LoadFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning(
                    "[NetConfigManager] LoadFromJson 警告：json 内容为空，保留当前配置继续运行。");
                return;
            }

            NetConfig loaded = null;
            bool parseFailed = false;
            System.Exception parseException = null;

            // 此处允许 try-catch：JSON 解析属于不可控的外部数据处理，
            // 解析失败不应阻断服务端运行，必须降级到默认配置
            try
            {
                loaded = UnityEngine.JsonUtility.FromJson<NetConfig>(json);
            }
            catch (System.Exception e)
            {
                parseFailed = true;
                parseException = e;
            }

            if (parseFailed || loaded == null)
            {
                Debug.LogWarning(
                    $"[NetConfigManager] LoadFromJson 警告：JSON 解析失败，保留当前配置继续运行。" +
                    $"异常信息：{parseException?.Message}");
                return;
            }

            Current = loaded;
            _onConfigReloaded?.Invoke(Current);
        }

        // 注册配置变更通知回调
        public void RegisterReloadCallback(System.Action<NetConfig> callback)
        {
            if (callback == null)
            {
                Debug.LogError("[NetConfigManager] RegisterReloadCallback 失败：callback 不得为 null");
                return;
            }

            _onConfigReloaded += callback;
        }

        // 注销配置变更通知回调
        public void UnregisterReloadCallback(System.Action<NetConfig> callback)
        {
            if (callback == null)
                return;

            _onConfigReloaded -= callback;
        }

        // 强制使用指定配置覆盖当前配置，用于测试与调试场景
        public void ForceOverride(NetConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[NetConfigManager] ForceOverride 失败：config 不得为 null");
                return;
            }

            Current = config;
            _onConfigReloaded?.Invoke(Current);
        }
    }
}
