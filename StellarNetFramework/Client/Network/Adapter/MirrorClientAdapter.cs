// Assets/StellarNetFramework/Client/Network/Adapter/MirrorClientAdapter.cs

using System;
using Mirror;
using UnityEngine;
using StellarNet.Shared.Protocol.Envelope;
using StellarNet.Shared.Enums;
using StellarNet.Server.Network.Adapter;

namespace StellarNet.Client.Network.Adapter
{
    // Mirror 客户端具体网络适配实现，是客户端框架内唯一允许直接依赖 Mirror 原生 API 的位置。
    // 职责：底层连接建立/断开/收包事件上抛，字节流与 NetworkEnvelope 的封装/解封装。
    // 编解码格式与服务端 MirrorNetworkAdapter 完全对称，必须保持一致。
    // 采用 NetworkClient 静态 API 驱动，挂载到客户端宿主 GameObject 上由 MonoBehaviour 生命周期驱动。
    public sealed class MirrorClientAdapter : IClientNetworkAdapter
    {
        // 头部编解码常量，必须与服务端 MirrorNetworkAdapter 保持完全一致
        private const int HeaderMessageIdSize = 4;
        private const int HeaderRoomIdLengthSize = 4;

        // 是否已注册 Mirror 回调
        private bool _isRegistered = false;

        // ── IClientNetworkAdapter 事件 ────────────────────────────────────────

        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<NetworkEnvelope> OnDataReceived;

        // ── IClientNetworkAdapter 实现 ────────────────────────────────────────

        public bool IsConnected => NetworkClient.isConnected;

        // 发起连接请求，注册 Mirror 客户端回调
        public void Connect(string host, int port)
        {
            if (string.IsNullOrEmpty(host))
            {
                Debug.LogError("[MirrorClientAdapter] Connect 失败：host 不得为空");
                return;
            }

            if (port <= 0 || port > 65535)
            {
                Debug.LogError(
                    $"[MirrorClientAdapter] Connect 失败：port 非法，port={port}，" +
                    $"有效范围为 1~65535");
                return;
            }

            if (NetworkClient.isConnected || NetworkClient.isConnecting)
            {
                Debug.LogWarning(
                    "[MirrorClientAdapter] Connect 警告：当前已处于连接或连接中状态，" +
                    "本次 Connect 请求已忽略。");
                return;
            }

            RegisterMirrorCallbacks();
            NetworkClient.Connect(host + ":" + port);
        }

        // 主动断开连接
        public void Disconnect()
        {
            if (!NetworkClient.isConnected && !NetworkClient.isConnecting)
            {
                Debug.LogWarning(
                    "[MirrorClientAdapter] Disconnect 警告：当前未处于连接状态，" +
                    "本次 Disconnect 请求已忽略。");
                return;
            }

            NetworkClient.Disconnect();
        }

        // 发送 NetworkEnvelope，编码为字节流后通过 Mirror 发送
        public void Send(NetworkEnvelope envelope, DeliveryMode deliveryMode)
        {
            if (!NetworkClient.isConnected)
            {
                Debug.LogError(
                    $"[MirrorClientAdapter] Send 失败：当前未连接到服务端，" +
                    $"MessageId={envelope?.MessageId}");
                return;
            }

            if (envelope == null)
            {
                Debug.LogError("[MirrorClientAdapter] Send 失败：envelope 不得为 null");
                return;
            }

            var bytes = EncodeEnvelope(envelope);
            if (bytes == null)
            {
                Debug.LogError(
                    $"[MirrorClientAdapter] Send 失败：NetworkEnvelope 编码结果为 null，" +
                    $"MessageId={envelope.MessageId}");
                return;
            }

            var channelId = ResolveChannel(deliveryMode);
            var msg = new FrameworkRawMessage { Data = bytes };
            NetworkClient.Send(msg, channelId);
        }

        // ── Mirror 回调注册 ───────────────────────────────────────────────────

        private void RegisterMirrorCallbacks()
        {
            if (_isRegistered)
                return;

            NetworkClient.OnConnectedEvent += OnMirrorConnected;
            NetworkClient.OnDisconnectedEvent += OnMirrorDisconnected;
            NetworkClient.RegisterHandler<FrameworkRawMessage>(OnMirrorDataReceived, false);

            _isRegistered = true;
        }

        // 注销 Mirror 回调，由 ClientInfrastructure 在关停阶段调用
        public void UnregisterCallbacks()
        {
            if (!_isRegistered)
                return;

            NetworkClient.OnConnectedEvent -= OnMirrorConnected;
            NetworkClient.OnDisconnectedEvent -= OnMirrorDisconnected;
            NetworkClient.UnregisterHandler<FrameworkRawMessage>();

            _isRegistered = false;
        }

        // ── Mirror 回调处理 ───────────────────────────────────────────────────

        private void OnMirrorConnected()
        {
            OnConnected?.Invoke();
        }

        private void OnMirrorDisconnected()
        {
            OnDisconnected?.Invoke("Mirror 客户端连接断开");
        }

        private void OnMirrorDataReceived(FrameworkRawMessage msg)
        {
            if (msg.Data == null || msg.Data.Length == 0)
            {
                Debug.LogError("[MirrorClientAdapter] OnMirrorDataReceived：收到空数据包，已忽略。");
                return;
            }

            var envelope = DecodeEnvelope(msg.Data);
            if (envelope == null)
            {
                Debug.LogError(
                    "[MirrorClientAdapter] OnMirrorDataReceived：NetworkEnvelope 解封装失败，" +
                    "数据包已丢弃。");
                return;
            }

            OnDataReceived?.Invoke(envelope);
        }

        // ── 编解码 ────────────────────────────────────────────────────────────

        // 编码格式与服务端 MirrorNetworkAdapter.EncodeEnvelope 完全对称
        private byte[] EncodeEnvelope(NetworkEnvelope envelope)
        {
            var roomIdBytes = System.Text.Encoding.UTF8.GetBytes(envelope.RoomId ?? string.Empty);
            var payloadBytes = envelope.Payload ?? Array.Empty<byte>();

            var totalLength = HeaderMessageIdSize + HeaderRoomIdLengthSize
                                                  + roomIdBytes.Length + payloadBytes.Length;
            var buffer = new byte[totalLength];
            var offset = 0;

            buffer[offset++] = (byte)(envelope.MessageId >> 24);
            buffer[offset++] = (byte)(envelope.MessageId >> 16);
            buffer[offset++] = (byte)(envelope.MessageId >> 8);
            buffer[offset++] = (byte)(envelope.MessageId);

            var roomIdLen = roomIdBytes.Length;
            buffer[offset++] = (byte)(roomIdLen >> 24);
            buffer[offset++] = (byte)(roomIdLen >> 16);
            buffer[offset++] = (byte)(roomIdLen >> 8);
            buffer[offset++] = (byte)(roomIdLen);

            if (roomIdLen > 0)
            {
                Buffer.BlockCopy(roomIdBytes, 0, buffer, offset, roomIdLen);
                offset += roomIdLen;
            }

            if (payloadBytes.Length > 0)
                Buffer.BlockCopy(payloadBytes, 0, buffer, offset, payloadBytes.Length);

            return buffer;
        }

        // 解码格式与服务端 MirrorNetworkAdapter.DecodeEnvelope 完全对称
        private NetworkEnvelope DecodeEnvelope(byte[] data)
        {
            var minHeaderSize = HeaderMessageIdSize + HeaderRoomIdLengthSize;
            if (data.Length < minHeaderSize)
            {
                Debug.LogError(
                    $"[MirrorClientAdapter] DecodeEnvelope 失败：数据长度 {data.Length} " +
                    $"小于最小头部长度 {minHeaderSize}，数据包格式非法。");
                return null;
            }

            var offset = 0;

            var messageId = (data[offset] << 24) | (data[offset + 1] << 16)
                                                 | (data[offset + 2] << 8) | data[offset + 3];
            offset += HeaderMessageIdSize;

            var roomIdLen = (data[offset] << 24) | (data[offset + 1] << 16)
                                                 | (data[offset + 2] << 8) | data[offset + 3];
            offset += HeaderRoomIdLengthSize;

            if (roomIdLen < 0 || offset + roomIdLen > data.Length)
            {
                Debug.LogError(
                    $"[MirrorClientAdapter] DecodeEnvelope 失败：RoomId 字节长度非法，" +
                    $"roomIdLen={roomIdLen}，数据总长={data.Length}，当前偏移={offset}。");
                return null;
            }

            var roomId = string.Empty;
            if (roomIdLen > 0)
            {
                roomId = System.Text.Encoding.UTF8.GetString(data, offset, roomIdLen);
                offset += roomIdLen;
            }

            var payloadLength = data.Length - offset;
            var payload = new byte[payloadLength];
            if (payloadLength > 0)
                Buffer.BlockCopy(data, offset, payload, 0, payloadLength);

            return new NetworkEnvelope(messageId, payload, roomId);
        }

        // 将框架 DeliveryMode 映射到 Mirror 发送通道 ID
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
                        $"[MirrorClientAdapter] 未知 DeliveryMode={deliveryMode}，" +
                        $"降级使用 ReliableOrdered 通道。");
                    return Channels.Reliable;
            }
        }
    }
}