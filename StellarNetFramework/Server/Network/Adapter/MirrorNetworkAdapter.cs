// Assets/StellarNetFramework/Server/Network/Adapter/MirrorNetworkAdapter.cs

using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.Envelope;
using StellarNet.Shared.Enums;

namespace StellarNet.Server.Network.Adapter
{
    // Mirror 具体网络适配实现，是框架内唯一允许直接依赖 Mirror 原生 API 的位置。
    // 职责：底层连接建立/断开/收包事件上抛，字节流与 NetworkEnvelope 的封装/解封装，
    //       Mirror 原生 connectionId(int) 到框架统一 ConnectionId 的双向映射。
    // 上层所有模块只感知框架统一 ConnectionId，不得穿透此层直接持有 Mirror 原生连接对象。
    // 采用 NetworkServer 静态 API 驱动，挂载到服务端宿主 GameObject 上由 MonoBehaviour 生命周期驱动。
    public sealed class MirrorNetworkAdapter : INetworkAdapter
    {
        // Mirror 原生 connectionId(int) → 框架 ConnectionId 的正向映射
        private readonly Dictionary<int, ConnectionId> _mirrorToFramework
            = new Dictionary<int, ConnectionId>();

        // 框架 ConnectionId → Mirror 原生 connectionId(int) 的反向映射
        // 发送时需要通过框架 ConnectionId 找回 Mirror 原生句柄
        private readonly Dictionary<ConnectionId, int> _frameworkToMirror
            = new Dictionary<ConnectionId, int>();

        // 框架 ConnectionId 自增计数器，从 0 开始，每次新连接接入时递增
        // 采用自增整数而非直接复用 Mirror connectionId，
        // 原因是 Mirror connectionId 在不同传输层实现中不保证唯一性语义
        private int _nextConnectionRawId = 0;

        // 是否已启动
        private bool _isStarted = false;

        // NetworkEnvelope 头部编码格式：
        //   [4字节 MessageId(int)] + [4字节 RoomId字符串字节长度(int)] + [N字节 RoomId UTF8] + [剩余字节 Payload]
        // 头部封装格式属于 Adapter/传输层职责，不依赖业务序列化器的对象图规则
        private const int HeaderMessageIdSize = 4;
        private const int HeaderRoomIdLengthSize = 4;

        // ── INetworkAdapter 事件 ──────────────────────────────────────────────

        public event Action<ConnectionId> OnConnected;
        public event Action<ConnectionId, string> OnDisconnected;
        public event Action<ConnectionId, NetworkEnvelope> OnDataReceived;

        // ── INetworkAdapter 生命周期 ──────────────────────────────────────────

        // 启动 Adapter，注册 Mirror NetworkServer 回调，开始监听连接
        // 必须在 NetworkServer.Listen() 之后调用
        public void Start()
        {
            if (_isStarted)
            {
                Debug.LogWarning("[MirrorNetworkAdapter] 重复调用 Start()，已忽略。");
                return;
            }

            // 注册 Mirror 服务端回调
            NetworkServer.OnConnectedEvent += OnMirrorConnected;
            NetworkServer.OnDisconnectedEvent += OnMirrorDisconnected;

            // 注册消息处理器，接收客户端上行的原始字节消息
            NetworkServer.RegisterHandler<FrameworkRawMessage>(OnMirrorDataReceived, requireAuthentication: false);

            _isStarted = true;
        }

        // 停止 Adapter，注销 Mirror 回调，由 GlobalInfrastructure.Shutdown() 最后调用
        public void Stop()
        {
            if (!_isStarted)
                return;

            NetworkServer.OnConnectedEvent -= OnMirrorConnected;
            NetworkServer.OnDisconnectedEvent -= OnMirrorDisconnected;
            NetworkServer.UnregisterHandler<FrameworkRawMessage>();

            _mirrorToFramework.Clear();
            _frameworkToMirror.Clear();
            _isStarted = false;
        }

        // 向指定连接发送 NetworkEnvelope。
        // 调用方必须在调用此方法前完成协议序列化、MessageId 解析、投递语义确定与上下文绑定。
        // Adapter 只负责将已完成绑定的 NetworkEnvelope 编码为字节流并通过 Mirror 发送。
        public void Send(ConnectionId connectionId, NetworkEnvelope envelope, DeliveryMode deliveryMode)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError(
                    $"[MirrorNetworkAdapter] Send 失败：ConnectionId 无效，" +
                    $"ConnectionId={connectionId}，MessageId={envelope?.MessageId}");
                return;
            }

            if (envelope == null)
            {
                Debug.LogError(
                    $"[MirrorNetworkAdapter] Send 失败：envelope 不得为 null，" +
                    $"ConnectionId={connectionId}");
                return;
            }

            if (!_frameworkToMirror.TryGetValue(connectionId, out var mirrorConnId))
            {
                Debug.LogError(
                    $"[MirrorNetworkAdapter] Send 失败：未找到 ConnectionId={connectionId} 对应的 Mirror 连接，" +
                    $"该连接可能已断开，MessageId={envelope.MessageId}");
                return;
            }

            var conn = NetworkServer.connections.GetValueOrDefault(mirrorConnId);
            if (conn == null)
            {
                Debug.LogError(
                    $"[MirrorNetworkAdapter] Send 失败：Mirror 连接对象为 null，" +
                    $"ConnectionId={connectionId}，MirrorConnId={mirrorConnId}，MessageId={envelope.MessageId}");
                return;
            }

            // 将 NetworkEnvelope 编码为字节流
            var bytes = EncodeEnvelope(envelope);
            if (bytes == null)
            {
                Debug.LogError(
                    $"[MirrorNetworkAdapter] Send 失败：NetworkEnvelope 编码结果为 null，" +
                    $"ConnectionId={connectionId}，MessageId={envelope.MessageId}");
                return;
            }

            // 根据投递语义选择 Mirror 发送通道
            // Mirror 使用 channelId：0 = Reliable，1 = Unreliable
            var channelId = ResolveChannel(deliveryMode);
            var msg = new FrameworkRawMessage { Data = bytes };
            conn.Send(msg, channelId);
        }

        // 断开指定连接，用于会话接管时强制踢出旧连接
        public void Disconnect(ConnectionId connectionId)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError(
                    $"[MirrorNetworkAdapter] Disconnect 失败：ConnectionId 无效，ConnectionId={connectionId}");
                return;
            }

            if (!_frameworkToMirror.TryGetValue(connectionId, out var mirrorConnId))
            {
                Debug.LogWarning(
                    $"[MirrorNetworkAdapter] Disconnect 警告：未找到 ConnectionId={connectionId} 对应的 Mirror 连接，" +
                    $"该连接可能已提前断开，本次断开请求已忽略。");
                return;
            }

            var conn = NetworkServer.connections.GetValueOrDefault(mirrorConnId);
            if (conn == null)
            {
                Debug.LogWarning(
                    $"[MirrorNetworkAdapter] Disconnect 警告：Mirror 连接对象为 null，" +
                    $"ConnectionId={connectionId}，MirrorConnId={mirrorConnId}，本次断开请求已忽略。");
                return;
            }

            conn.Disconnect();
        }

        // ── Mirror 回调处理 ───────────────────────────────────────────────────

        // Mirror 新连接建立回调
        // 在此处完成 Mirror 原生 connectionId → 框架 ConnectionId 的唯一映射
        private void OnMirrorConnected(NetworkConnectionToClient conn)
        {
            if (conn == null)
            {
                Debug.LogError("[MirrorNetworkAdapter] OnMirrorConnected：收到 null 连接对象，已忽略。");
                return;
            }

            // 防止重复映射
            if (_mirrorToFramework.ContainsKey(conn.connectionId))
            {
                Debug.LogWarning(
                    $"[MirrorNetworkAdapter] OnMirrorConnected：Mirror connectionId={conn.connectionId} " +
                    $"已存在映射，可能是重复连接事件，已忽略。");
                return;
            }

            var frameworkConnId = new ConnectionId(_nextConnectionRawId++);
            _mirrorToFramework[conn.connectionId] = frameworkConnId;
            _frameworkToMirror[frameworkConnId] = conn.connectionId;

            OnConnected?.Invoke(frameworkConnId);
        }

        // Mirror 连接断开回调
        private void OnMirrorDisconnected(NetworkConnectionToClient conn)
        {
            if (conn == null)
            {
                Debug.LogError("[MirrorNetworkAdapter] OnMirrorDisconnected：收到 null 连接对象，已忽略。");
                return;
            }

            if (!_mirrorToFramework.TryGetValue(conn.connectionId, out var frameworkConnId))
            {
                Debug.LogWarning(
                    $"[MirrorNetworkAdapter] OnMirrorDisconnected：Mirror connectionId={conn.connectionId} " +
                    $"未找到对应框架 ConnectionId 映射，可能已提前清理，已忽略。");
                return;
            }

            // 清理双向映射
            _mirrorToFramework.Remove(conn.connectionId);
            _frameworkToMirror.Remove(frameworkConnId);

            OnDisconnected?.Invoke(frameworkConnId, $"Mirror 连接断开，MirrorConnId={conn.connectionId}");
        }

        // Mirror 收包回调，完成字节流解封装为 NetworkEnvelope 后上抛
        private void OnMirrorDataReceived(NetworkConnectionToClient conn, FrameworkRawMessage msg)
        {
            if (conn == null)
            {
                Debug.LogError("[MirrorNetworkAdapter] OnMirrorDataReceived：conn 为 null，已忽略。");
                return;
            }

            if (msg.Data == null || msg.Data.Length == 0)
            {
                Debug.LogError(
                    $"[MirrorNetworkAdapter] OnMirrorDataReceived：收到空数据包，" +
                    $"MirrorConnId={conn.connectionId}，已忽略。");
                return;
            }

            if (!_mirrorToFramework.TryGetValue(conn.connectionId, out var frameworkConnId))
            {
                Debug.LogError(
                    $"[MirrorNetworkAdapter] OnMirrorDataReceived：Mirror connectionId={conn.connectionId} " +
                    $"未找到对应框架 ConnectionId 映射，数据包已丢弃。");
                return;
            }

            // 解封装字节流为 NetworkEnvelope
            var envelope = DecodeEnvelope(msg.Data);
            if (envelope == null)
            {
                Debug.LogError(
                    $"[MirrorNetworkAdapter] OnMirrorDataReceived：NetworkEnvelope 解封装失败，" +
                    $"ConnectionId={frameworkConnId}，数据包已丢弃。");
                return;
            }

            OnDataReceived?.Invoke(frameworkConnId, envelope);
        }

        // ── 编解码 ────────────────────────────────────────────────────────────

        // 将 NetworkEnvelope 编码为字节流
        // 格式：[4字节 MessageId] + [4字节 RoomId字节长度] + [N字节 RoomId UTF8] + [Payload]
        private byte[] EncodeEnvelope(NetworkEnvelope envelope)
        {
            var roomIdBytes = System.Text.Encoding.UTF8.GetBytes(envelope.RoomId ?? string.Empty);
            var payloadBytes = envelope.Payload ?? Array.Empty<byte>();

            var totalLength = HeaderMessageIdSize + HeaderRoomIdLengthSize
                              + roomIdBytes.Length + payloadBytes.Length;
            var buffer = new byte[totalLength];
            var offset = 0;

            // 写入 MessageId（大端序）
            buffer[offset++] = (byte)(envelope.MessageId >> 24);
            buffer[offset++] = (byte)(envelope.MessageId >> 16);
            buffer[offset++] = (byte)(envelope.MessageId >> 8);
            buffer[offset++] = (byte)(envelope.MessageId);

            // 写入 RoomId 字节长度
            var roomIdLen = roomIdBytes.Length;
            buffer[offset++] = (byte)(roomIdLen >> 24);
            buffer[offset++] = (byte)(roomIdLen >> 16);
            buffer[offset++] = (byte)(roomIdLen >> 8);
            buffer[offset++] = (byte)(roomIdLen);

            // 写入 RoomId UTF8 字节
            if (roomIdLen > 0)
            {
                Buffer.BlockCopy(roomIdBytes, 0, buffer, offset, roomIdLen);
                offset += roomIdLen;
            }

            // 写入 Payload
            if (payloadBytes.Length > 0)
                Buffer.BlockCopy(payloadBytes, 0, buffer, offset, payloadBytes.Length);

            return buffer;
        }

        // 将字节流解封装为 NetworkEnvelope
        // 解封装失败返回 null，由调用方决定是否丢弃
        private NetworkEnvelope DecodeEnvelope(byte[] data)
        {
            var minHeaderSize = HeaderMessageIdSize + HeaderRoomIdLengthSize;
            if (data.Length < minHeaderSize)
            {
                Debug.LogError(
                    $"[MirrorNetworkAdapter] DecodeEnvelope 失败：数据长度 {data.Length} " +
                    $"小于最小头部长度 {minHeaderSize}，数据包格式非法。");
                return null;
            }

            var offset = 0;

            // 读取 MessageId
            var messageId = (data[offset] << 24) | (data[offset + 1] << 16)
                            | (data[offset + 2] << 8) | data[offset + 3];
            offset += HeaderMessageIdSize;

            // 读取 RoomId 字节长度
            var roomIdLen = (data[offset] << 24) | (data[offset + 1] << 16)
                            | (data[offset + 2] << 8) | data[offset + 3];
            offset += HeaderRoomIdLengthSize;

            if (roomIdLen < 0 || offset + roomIdLen > data.Length)
            {
                Debug.LogError(
                    $"[MirrorNetworkAdapter] DecodeEnvelope 失败：RoomId 字节长度非法，" +
                    $"roomIdLen={roomIdLen}，数据总长={data.Length}，当前偏移={offset}。");
                return null;
            }

            // 读取 RoomId
            var roomId = string.Empty;
            if (roomIdLen > 0)
            {
                roomId = System.Text.Encoding.UTF8.GetString(data, offset, roomIdLen);
                offset += roomIdLen;
            }

            // 读取 Payload
            var payloadLength = data.Length - offset;
            var payload = new byte[payloadLength];
            if (payloadLength > 0)
                Buffer.BlockCopy(data, offset, payload, 0, payloadLength);

            return new NetworkEnvelope(messageId, payload, roomId);
        }

        // 将框架 DeliveryMode 映射到 Mirror 发送通道 ID
        // Mirror 通道约定：0 = Reliable（有序/无序均走此通道），1 = Unreliable
        private int ResolveChannel(DeliveryMode deliveryMode)
        {
            switch (deliveryMode)
            {
                case DeliveryMode.ReliableOrdered:
                case DeliveryMode.ReliableUnordered:
                    return Channels.Reliable;
                case DeliveryMode.UnreliableLatest:
                    return Channels.Unreliable;
                default:
                    Debug.LogWarning(
                        $"[MirrorNetworkAdapter] 未知 DeliveryMode={deliveryMode}，" +
                        $"降级使用 ReliableOrdered 通道。");
                    return Channels.Reliable;
            }
        }

        // 通过框架 ConnectionId 查询对应的 Mirror 原生 connectionId，用于诊断
        public bool TryGetMirrorConnectionId(ConnectionId frameworkConnId, out int mirrorConnId)
        {
            return _frameworkToMirror.TryGetValue(frameworkConnId, out mirrorConnId);
        }

        // 当前已映射的活跃连接数量，用于诊断
        public int ActiveConnectionCount => _mirrorToFramework.Count;
    }

    // Mirror 消息载体，用于在框架层传输原始字节流。
    // 定义在此处是因为它只服务于 MirrorNetworkAdapter 的收发管线，
    // 不属于业务协议，不需要放入 Shared 层。
    public struct FrameworkRawMessage : NetworkMessage
    {
        public byte[] Data;
    }
}
