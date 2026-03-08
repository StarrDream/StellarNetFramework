using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Client.Room
{
    /// <summary>
    /// 客户端房间组件注册表。
    /// 负责维护稳定组件标识到客户端组件工厂的映射。
    /// 由 ClientInfrastructure 在初始化阶段构建，供 ClientRoomAssembler 使用。
    /// </summary>
    public sealed class ClientRoomComponentRegistry
    {
        private readonly Dictionary<string, ClientRoomAssembler.ClientComponentFactory> _factories
            = new Dictionary<string, ClientRoomAssembler.ClientComponentFactory>();

        /// <summary>
        /// 注册客户端组件工厂。
        /// componentId 必须与服务端保持一致。
        /// </summary>
        public void Register(string componentId, ClientRoomAssembler.ClientComponentFactory factory)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                Debug.LogError("[ClientRoomComponentRegistry] Register 失败：componentId 为空。");
                return;
            }

            if (factory == null)
            {
                Debug.LogError($"[ClientRoomComponentRegistry] Register 失败：factory 为 null，componentId={componentId}。");
                return;
            }

            if (_factories.ContainsKey(componentId))
            {
                Debug.LogError($"[ClientRoomComponentRegistry] Register 失败：componentId={componentId} 已重复注册。");
                return;
            }

            _factories[componentId] = factory;
        }

        /// <summary>
        /// 获取注册表字典副本，供装配器使用。
        /// </summary>
        public Dictionary<string, ClientRoomAssembler.ClientComponentFactory> GetRegistry()
        {
            return new Dictionary<string, ClientRoomAssembler.ClientComponentFactory>(_factories);
        }
    }
}