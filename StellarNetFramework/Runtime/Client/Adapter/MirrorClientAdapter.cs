// ════════════════════════════════════════════════════════════════
// 文件：MirrorClientAdapter.cs
// 路径：Assets/StellarNetFramework/Runtime/Client/Adapter/MirrorClientAdapter.cs
// 职责：Mirror 客户端网络适配器。
//       修正：移除私有 FrameworkRawMessage 定义，使用 Shared.Network.FrameworkRawMessage，
//       解决类型哈希不匹配导致的 "failed to unpack" 错误。
// ════════════════════════════════════════════════════════════════

using System;
using Mirror;
using StellarNet.Shared.Envelope;
using StellarNet.Shared.Network; // 引入 Shared 命名空间
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Client.Adapter
{
    /// <summary>
    /// Mirror 客户端网络适配器，负责框架与 Mirror 底层网络库的客户端边界隔离。
    /// 只使用 Mirror 的基础通讯能力：连接建立/断开事件与字节收发，严禁使用 SyncVar 等高级特性。
    /// 客户端只存在一条连接，不需要 ConnectionId 映射，直接使用 Mirror 的 NetworkClient。
    /// 上层通过订阅事件接收连接通知与数据包，Adapter 不处理任何业务状态。
    /// </summary>
    public sealed class MirrorClientAdapter : NetworkManager
    {
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<NetworkEnvelope> OnDataReceived;

        private ISerializer _serializer;
        private bool _isHandlerRegistered;

        /// <summary>
        /// 由 ClientInfrastructure 在装配阶段调用，注入序列化器依赖。
        /// </summary>
        public void Initialize(ISerializer serializer)
        {
            if (serializer == null)
            {
                Debug.LogError($"[MirrorClientAdapter] Initialize 失败：物体 {name} 注入的 serializer 为 null。");
                return;
            }

            _serializer = serializer;
        }

        /// <summary>
        /// 发起连接，由 ClientInfrastructure 在装配完成后显式调用。
        /// </summary>
        public void Connect(string serverAddress, int serverPort)
        {
            if (_serializer == null)
            {
                Debug.LogError($"[MirrorClientAdapter] Connect 失败：物体 {name} 的 _serializer 为 null，请先调用 Initialize()。");
                return;
            }

            if (string.IsNullOrEmpty(serverAddress))
            {
                Debug.LogError($"[MirrorClientAdapter] Connect 失败：serverAddress 为空，物体 {name}。");
                return;
            }

            if (serverPort <= 0 || serverPort > 65535)
            {
                Debug.LogError($"[MirrorClientAdapter] Connect 失败：serverPort={serverPort} 非法，物体 {name}。");
                return;
            }

            networkAddress = serverAddress;

            // 使用 Shared 层定义的 FrameworkRawMessage，确保 ID 一致
            if (!_isHandlerRegistered)
            {
                NetworkClient.RegisterHandler<FrameworkRawMessage>(OnMirrorMessageReceived);
                _isHandlerRegistered = true;
            }

            Debug.Log($"[MirrorClientAdapter] 发起连接，地址={serverAddress}，端口={serverPort}，物体={name}。");
            StartClient();
        }

        /// <summary>
        /// 断开连接，由 ClientInfrastructure 在 Shutdown 阶段显式调用。
        /// </summary>
        public void Disconnect()
        {
            if (_isHandlerRegistered)
            {
                NetworkClient.UnregisterHandler<FrameworkRawMessage>();
                _isHandlerRegistered = false;
            }

            StopClient();
            Debug.Log($"[MirrorClientAdapter] 连接已断开，物体={name}。");
        }

        /// <summary>
        /// 向服务端发送 NetworkEnvelope。
        /// 此方法只负责底层传输，不负责任何业务语义决策。
        /// </summary>
        public void Send(NetworkEnvelope envelope)
        {
            if (envelope == null)
            {
                Debug.LogError($"[MirrorClientAdapter] Send 失败：envelope 为 null，物体={name}。");
                return;
            }

            if (!NetworkClient.isConnected)
            {
                Debug.LogError($"[MirrorClientAdapter] Send 失败：当前未连接到服务端，MessageId={envelope.MessageId}，物体={name}。");
                return;
            }

            if (_serializer == null)
            {
                Debug.LogError(
                    $"[MirrorClientAdapter] Send 失败：_serializer 为 null，MessageId={envelope.MessageId}，物体={name}。");
                return;
            }

            byte[] envelopeBytes = _serializer.Serialize(envelope);
            if (envelopeBytes == null)
            {
                Debug.LogError(
                    $"[MirrorClientAdapter] Send 失败：NetworkEnvelope 序列化结果为 null，MessageId={envelope.MessageId}，物体={name}。");
                return;
            }

            // 使用 Shared 层定义的 FrameworkRawMessage
            var rawMsg = new FrameworkRawMessage { Data = envelopeBytes };
            NetworkClient.Send(rawMsg);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            Debug.Log($"[MirrorClientAdapter] 连接服务端成功，物体={name}。");
            OnConnectedToServer?.Invoke();
        }

        public override void OnClientDisconnect()
        {
            Debug.Log($"[MirrorClientAdapter] 与服务端断开连接，物体={name}。");
            OnDisconnectedFromServer?.Invoke();
            base.OnClientDisconnect();
        }

        /// <summary>
        /// 接收服务端字节消息，解封装为 NetworkEnvelope 后上抛给框架核心层。
        /// </summary>
        private void OnMirrorMessageReceived(FrameworkRawMessage rawMsg)
        {
            if (rawMsg.Data == null || rawMsg.Data.Length == 0)
            {
                Debug.LogError($"[MirrorClientAdapter] 收到空数据包，已丢弃，物体={name}。");
                return;
            }

            if (_serializer == null)
            {
                Debug.LogError($"[MirrorClientAdapter] _serializer 为 null，无法解封装数据包，已丢弃，物体={name}。");
                return;
            }

            var envelope = _serializer.Deserialize<NetworkEnvelope>(rawMsg.Data);
            if (envelope == null)
            {
                Debug.LogError($"[MirrorClientAdapter] NetworkEnvelope 反序列化失败，已丢弃，物体={name}。");
                return;
            }

            OnDataReceived?.Invoke(envelope);
        }
    }
}