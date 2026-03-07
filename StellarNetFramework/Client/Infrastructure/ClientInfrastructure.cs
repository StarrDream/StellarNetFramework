// Assets/StellarNetFramework/Client/Infrastructure/ClientInfrastructure.cs

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using StellarNet.Client.Network.Adapter;
using StellarNet.Client.Network.Entry;
using StellarNet.Client.Network.Router;
using StellarNet.Client.Network.Sender;
using StellarNet.Client.Session;
using StellarNet.Client.Replay;

namespace StellarNet.Client.Infrastructure
{
    // 客户端顶级门面，是整个框架客户端的唯一装配入口与生命周期驱动者。
    // 职责：
    //   1. 按依赖顺序完成所有客户端基础设施模块的构建与装配
    //   2. 绑定 Adapter 事件到 ClientNetworkEntry 与 ClientSessionContext
    //   3. 统一驱动每帧 Tick（回放模式下驱动 ReplayPlaybackController）
    //   4. 统一执行关停流程
    // 业务层只需继承 ClientInfrastructure 并实现 OnConfigure() 完成业务委托注入，
    // 不得绕过本类直接操作任何基础设施模块。
    public abstract class ClientInfrastructure : MonoBehaviour
    {
        // ── 基础设施模块（只读属性，供业务层子类在 OnConfigure() 中访问）──────

        protected MessageRegistry MessageRegistry { get; private set; }
        protected IClientNetworkAdapter NetworkAdapter { get; private set; }
        protected ClientSessionContext SessionContext { get; private set; }
        protected ClientGlobalMessageRouter GlobalRouter { get; private set; }
        protected ClientRoomMessageRouter RoomRouter { get; private set; }
        protected ClientNetworkEntry NetworkEntry { get; private set; }
        protected ClientMessageSender MessageSender { get; private set; }
        protected ClientReplayPlaybackController ReplayController { get; private set; }

        // 当前帧时间戳（Unix 毫秒），每帧 Tick 开始时更新
        protected long NowUnixMs { get; private set; }

        // 是否处于回放模式（回放模式下 Tick 驱动 ReplayController，不处理正常网络消息）
        protected bool IsReplayMode { get; private set; }

        // 是否已完成初始化
        private bool _isInitialized = false;

        // 是否已执行关停
        private bool _isShutdown = false;

        // ── Unity 生命周期 ────────────────────────────────────────────────────

        private void Awake()
        {
            NowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            BuildInfrastructure();
        }

        private void Update()
        {
            if (!_isInitialized || _isShutdown)
                return;

            NowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (IsReplayMode)
            {
                // 回放模式：只驱动 ReplayController，不处理正常网络消息
                ReplayController?.Tick(NowUnixMs);
            }

            OnTick((long)(Time.deltaTime * 1000f), NowUnixMs);
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        // ── 装配流程 ──────────────────────────────────────────────────────────

        private void BuildInfrastructure()
        {
            // 步骤一：构建 MessageRegistry（程序集白名单由子类提供）
            var assemblies = GetMessageRegistryAssemblies();
            if (assemblies == null)
            {
                Debug.LogError(
                    $"[ClientInfrastructure] 装配失败：GetMessageRegistryAssemblies() 返回 null，" +
                    $"物体={name}");
                return;
            }

            MessageRegistry = MessageRegistryBuilder.Build(assemblies);

            // 步骤二：构建序列化器（由子类提供具体实现）
            var serializer = CreateSerializer();
            if (serializer == null)
            {
                Debug.LogError(
                    $"[ClientInfrastructure] 装配失败：CreateSerializer() 返回 null，物体={name}");
                return;
            }

            // 步骤三：构建 Adapter（由子类提供具体实现）
            NetworkAdapter = CreateNetworkAdapter();
            if (NetworkAdapter == null)
            {
                Debug.LogError(
                    $"[ClientInfrastructure] 装配失败：CreateNetworkAdapter() 返回 null，物体={name}");
                return;
            }

            // 步骤四：构建会话上下文
            SessionContext = new ClientSessionContext();

            // 步骤五：构建路由器
            GlobalRouter = new ClientGlobalMessageRouter();
            RoomRouter = new ClientRoomMessageRouter();

            // 步骤六：构建网络入口
            NetworkEntry = new ClientNetworkEntry(
                MessageRegistry, serializer, SessionContext, GlobalRouter, RoomRouter);

            // 步骤七：构建发送器
            MessageSender = new ClientMessageSender(
                MessageRegistry, serializer, NetworkAdapter, SessionContext);

            // 步骤八：构建回放控制器
            ReplayController = new ClientReplayPlaybackController(
                MessageRegistry, serializer, RoomRouter);

            // 步骤九：绑定 Adapter 事件
            NetworkAdapter.OnConnected += OnAdapterConnected;
            NetworkAdapter.OnDisconnected += OnAdapterDisconnected;
            NetworkAdapter.OnDataReceived += NetworkEntry.OnAdapterDataReceived;

            // 步骤十：调用业务层配置扩展点
            OnConfigure();

            _isInitialized = true;
        }

        // ── 连接事件处理 ──────────────────────────────────────────────────────

        private void OnAdapterConnected()
        {
            SessionContext.OnConnected();
            OnConnectionEstablished();
        }

        private void OnAdapterDisconnected(string reason)
        {
            SessionContext.OnDisconnected();
            OnConnectionLost(reason);
        }

        // ── 回放模式控制 ──────────────────────────────────────────────────────

        // 进入回放模式，断开当前网络连接（如有），切换 Tick 驱动到 ReplayController
        protected void EnterReplayMode()
        {
            if (IsReplayMode)
            {
                Debug.LogWarning(
                    "[ClientInfrastructure] EnterReplayMode 警告：已处于回放模式，本次调用已忽略。");
                return;
            }

            if (NetworkAdapter.IsConnected)
                NetworkAdapter.Disconnect();

            IsReplayMode = true;
        }

        // 退出回放模式，停止 ReplayController，恢复正常网络模式
        protected void ExitReplayMode()
        {
            if (!IsReplayMode)
            {
                Debug.LogWarning(
                    "[ClientInfrastructure] ExitReplayMode 警告：当前不处于回放模式，本次调用已忽略。");
                return;
            }

            ReplayController?.Stop();
            IsReplayMode = false;
        }

        // ── 连接便捷方法（供业务层子类调用）─────────────────────────────────

        // 发起连接请求，参数由子类从配置或 UI 获取后传入
        protected void Connect(string host, int port)
        {
            if (string.IsNullOrEmpty(host))
            {
                Debug.LogError(
                    $"[ClientInfrastructure] Connect 失败：host 不得为空，物体={name}");
                return;
            }

            if (port <= 0 || port > 65535)
            {
                Debug.LogError(
                    $"[ClientInfrastructure] Connect 失败：port 非法，port={port}，物体={name}");
                return;
            }

            NetworkAdapter.Connect(host, port);
        }

        // 主动断开连接
        protected void Disconnect()
        {
            if (!NetworkAdapter.IsConnected)
            {
                Debug.LogWarning(
                    $"[ClientInfrastructure] Disconnect 警告：当前未连接，物体={name}");
                return;
            }

            NetworkAdapter.Disconnect();
        }

        // ── 会话上下文便捷维护方法（供业务层 Handler 回调中调用）─────────────

        // 认证成功后由业务层 Handler 调用，写入服务端签发的 SessionId
        protected void OnAuthenticationSuccess(Shared.Identity.SessionId sessionId)
        {
            if (!sessionId.IsValid)
            {
                Debug.LogError(
                    $"[ClientInfrastructure] OnAuthenticationSuccess 失败：sessionId 无效，物体={name}");
                return;
            }

            SessionContext.OnAuthenticated(sessionId);
        }

        // 加房成功后由业务层 Handler 调用，写入当前房间 ID
        protected void OnJoinRoomSuccess(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError(
                    $"[ClientInfrastructure] OnJoinRoomSuccess 失败：roomId 不得为空，物体={name}");
                return;
            }

            SessionContext.OnJoinedRoom(roomId);
        }

        // 离房后由业务层 Handler 调用，清空当前房间 ID 并清理房间域 Handler
        protected void OnLeaveRoom()
        {
            SessionContext.OnLeftRoom();

            // 离房时清空房间域路由器，防止旧房间消息误路由到新房间 Handler
            RoomRouter.Clear();
        }

        // ── 关停流程 ──────────────────────────────────────────────────────────

        private void Shutdown()
        {
            if (_isShutdown)
                return;

            _isShutdown = true;

            if (NetworkAdapter != null)
            {
                // 先解绑事件，防止关停过程中触发回调产生脏数据
                NetworkAdapter.OnConnected -= OnAdapterConnected;
                NetworkAdapter.OnDisconnected -= OnAdapterDisconnected;
                NetworkAdapter.OnDataReceived -= NetworkEntry.OnAdapterDataReceived;

                if (NetworkAdapter.IsConnected)
                    NetworkAdapter.Disconnect();

                // 注销 Mirror 回调（MirrorClientAdapter 特有能力）
                var mirrorAdapter = NetworkAdapter as MirrorClientAdapter;
                mirrorAdapter?.UnregisterCallbacks();
            }

            GlobalRouter?.Clear();
            RoomRouter?.Clear();
            ReplayController?.Stop();
            SessionContext?.Reset();

            OnShutdown();
        }

        // ── 抽象与虚方法（业务层子类实现）────────────────────────────────────

        // 返回用于构建 MessageRegistry 的程序集白名单，必须由业务层子类实现
        protected abstract IEnumerable<Assembly> GetMessageRegistryAssemblies();

        // 返回序列化器实例，必须由业务层子类实现
        protected abstract ISerializer CreateSerializer();

        // 返回 NetworkAdapter 实例，必须由业务层子类实现
        // 典型实现：return new MirrorClientAdapter();
        protected abstract IClientNetworkAdapter CreateNetworkAdapter();

        // 业务层配置扩展点，在所有基础设施模块构建完成后调用
        // 业务层在此方法内完成：Handler 注册、会话回调绑定等
        protected abstract void OnConfigure();

        // 每帧业务层扩展点，在框架 Tick 完成后调用
        protected virtual void OnTick(long deltaTimeMs, long nowUnixMs) { }

        // 连接建立扩展点，业务层可重写以实现连接建立时的额外逻辑
        // 典型用途：连接建立后立即发送认证请求
        protected virtual void OnConnectionEstablished() { }

        // 连接断开扩展点，业务层可重写以实现断线处理逻辑
        // 典型用途：触发重连 UI、清理局内状态
        protected virtual void OnConnectionLost(string reason) { }

        // 关停扩展点，业务层可重写以实现关停时的额外清理逻辑
        protected virtual void OnShutdown() { }
    }
}
