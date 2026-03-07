// Assets/StellarNetFramework/Server/Session/SessionManager.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Server.Infrastructure.GlobalScope;

namespace StellarNet.Server.Session
{
    // 服务端会话管理器，负责 SessionId 签发、会话绑定、连接接管与超时管理。
    // 任意时刻，一个 SessionId 只允许存在一个有效主连接。
    // 新连接完成认证或重连认证成功后，旧连接必须立即标记为 Replaced。
    // 被标记为 Replaced 的旧连接，其后续所有来包一律拒收。
    // 断线后 SessionData 可继续保留到其独立超时结束，不随连接断开立即销毁。
    // Session 保留超时与 Room 空置销毁超时必须独立配置、独立计时、独立生效。
    public sealed class SessionManager : IGlobalService
    {
        // SessionId → SessionData 主索引
        private readonly Dictionary<string, SessionData> _sessionById
            = new Dictionary<string, SessionData>();

        // ConnectionId → SessionId 反向索引，用于收包时快速定位会话
        private readonly Dictionary<ConnectionId, string> _sessionByConnection
            = new Dictionary<ConnectionId, string>();

        // Session 保留超时时长（毫秒），断线后超过此时长未重连则销毁会话
        private long _sessionRetainTimeoutMs;

        // SessionId 生成计数器
        private int _sessionCounter = 0;

        public SessionManager(long sessionRetainTimeoutMs = 30000)
        {
            _sessionRetainTimeoutMs = sessionRetainTimeoutMs;
        }

        // 更新 Session 保留超时时长，由 NetConfigManager 热重载时调用
        public void UpdateSessionRetainTimeout(long timeoutMs)
        {
            _sessionRetainTimeoutMs = timeoutMs;
        }

        // ── 会话签发 ──────────────────────────────────────────────────────────

        // 为新连接签发全新会话，返回签发的 SessionData。
        public SessionData CreateSession(ConnectionId connectionId, long nowUnixMs)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError(
                    $"[SessionManager] CreateSession 失败：ConnectionId 无效，ConnectionId={connectionId}");
                return null;
            }

            var sessionIdValue = $"SNF-{_sessionCounter++}-{nowUnixMs % 1000000}";
            var sessionId = new SessionId(sessionIdValue);
            var sessionData = new SessionData(sessionId, connectionId, nowUnixMs);

            _sessionById[sessionIdValue] = sessionData;
            _sessionByConnection[connectionId] = sessionIdValue;

            return sessionData;
        }

        // ── 连接接管 ──────────────────────────────────────────────────────────

        // 重连接管：将已有会话绑定到新连接，旧连接标记为 Replaced。
        public SessionData TakeoverSession(SessionId sessionId, ConnectionId newConnectionId, long nowUnixMs)
        {
            if (!sessionId.IsValid)
            {
                Debug.LogError(
                    $"[SessionManager] TakeoverSession 失败：SessionId 无效，" +
                    $"NewConnectionId={newConnectionId}");
                return null;
            }

            if (!newConnectionId.IsValid)
            {
                Debug.LogError(
                    $"[SessionManager] TakeoverSession 失败：新 ConnectionId 无效，" +
                    $"SessionId={sessionId}");
                return null;
            }

            if (!_sessionById.TryGetValue(sessionId.Value, out var sessionData))
            {
                Debug.LogError(
                    $"[SessionManager] TakeoverSession 失败：SessionId={sessionId} 不存在，" +
                    $"NewConnectionId={newConnectionId}");
                return null;
            }

            if (IsSessionExpired(sessionData, nowUnixMs))
            {
                Debug.LogError(
                    $"[SessionManager] TakeoverSession 失败：SessionId={sessionId} 已超时，" +
                    $"LastActiveUnixMs={sessionData.LastActiveUnixMs}，NowUnixMs={nowUnixMs}，" +
                    $"超时阈值={_sessionRetainTimeoutMs}ms");
                RemoveSession(sessionId);
                return null;
            }

            var oldConnectionId = sessionData.ConnectionId;
            if (oldConnectionId.IsValid)
            {
                sessionData.IsReplaced = true;
                _sessionByConnection.Remove(oldConnectionId);
            }

            sessionData.ConnectionId = newConnectionId;
            sessionData.IsReplaced = false;
            sessionData.LastActiveUnixMs = nowUnixMs;
            _sessionByConnection[newConnectionId] = sessionId.Value;

            return sessionData;
        }

        // ── 查询 ──────────────────────────────────────────────────────────────

        public SessionData GetSessionById(SessionId sessionId)
        {
            if (!sessionId.IsValid)
                return null;

            _sessionById.TryGetValue(sessionId.Value, out var sessionData);
            return sessionData;
        }

        public SessionData GetSessionByConnection(ConnectionId connectionId)
        {
            if (!connectionId.IsValid)
                return null;

            if (!_sessionByConnection.TryGetValue(connectionId, out var sessionIdValue))
                return null;

            _sessionById.TryGetValue(sessionIdValue, out var sessionData);
            return sessionData;
        }

        public bool IsConnectionValid(ConnectionId connectionId)
        {
            var session = GetSessionByConnection(connectionId);
            return session != null && !session.IsReplaced;
        }

        // ── 公开枚举方法（供 GlobalInfrastructure 调用，替代反射访问）────────

        // 枚举全部当前在线会话，用于全体广播目标集合构建
        public IEnumerable<SessionData> GetAllOnlineSessions()
        {
            foreach (var kv in _sessionById)
            {
                if (kv.Value.IsOnline)
                    yield return kv.Value;
            }
        }

        // 枚举归属于指定房间的全部会话（含离线），用于房间销毁时清理会话房间归属
        public IEnumerable<SessionData> GetAllSessionsInRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
                yield break;

            foreach (var kv in _sessionById)
            {
                if (string.Equals(kv.Value.CurrentRoomId, roomId, StringComparison.Ordinal))
                    yield return kv.Value;
            }
        }

        // ── 连接断开处理 ──────────────────────────────────────────────────────

        public void OnConnectionDisconnected(ConnectionId connectionId, long nowUnixMs)
        {
            if (!connectionId.IsValid)
                return;

            if (!_sessionByConnection.TryGetValue(connectionId, out var sessionIdValue))
                return;

            _sessionByConnection.Remove(connectionId);

            if (_sessionById.TryGetValue(sessionIdValue, out var sessionData))
            {
                sessionData.ConnectionId = ConnectionId.Invalid;
                sessionData.LastActiveUnixMs = nowUnixMs;
            }
        }

        // ── 房间归属维护 ──────────────────────────────────────────────────────

        public void SetSessionRoom(SessionId sessionId, string roomId)
        {
            if (!sessionId.IsValid)
            {
                Debug.LogError(
                    $"[SessionManager] SetSessionRoom 失败：SessionId 无效，RoomId={roomId}");
                return;
            }

            if (!_sessionById.TryGetValue(sessionId.Value, out var sessionData))
            {
                Debug.LogError(
                    $"[SessionManager] SetSessionRoom 失败：SessionId={sessionId} 不存在，RoomId={roomId}");
                return;
            }

            sessionData.CurrentRoomId = roomId ?? string.Empty;
        }

        public void ClearSessionRoom(SessionId sessionId)
        {
            if (!sessionId.IsValid)
                return;

            if (!_sessionById.TryGetValue(sessionId.Value, out var sessionData))
                return;

            sessionData.CurrentRoomId = string.Empty;
        }

        // ── 超时巡检 ──────────────────────────────────────────────────────────

        public void TickSessionExpireCheck(long nowUnixMs)
        {
            var expiredKeys = new List<string>();

            foreach (var kv in _sessionById)
            {
                var session = kv.Value;

                if (session.IsOnline)
                    continue;

                if (IsSessionExpired(session, nowUnixMs))
                    expiredKeys.Add(kv.Key);
            }

            foreach (var key in expiredKeys)
            {
                _sessionById.Remove(key);
            }
        }

        // ── 销毁 ──────────────────────────────────────────────────────────────

        public void RemoveSession(SessionId sessionId)
        {
            if (!sessionId.IsValid)
                return;

            if (!_sessionById.TryGetValue(sessionId.Value, out var sessionData))
                return;

            if (sessionData.ConnectionId.IsValid)
                _sessionByConnection.Remove(sessionData.ConnectionId);

            _sessionById.Remove(sessionId.Value);
        }

        public void Clear()
        {
            _sessionById.Clear();
            _sessionByConnection.Clear();
        }

        // ── 诊断 ──────────────────────────────────────────────────────────────

        public int OnlineSessionCount
        {
            get
            {
                var count = 0;
                foreach (var kv in _sessionById)
                {
                    if (kv.Value.IsOnline)
                        count++;
                }

                return count;
            }
        }

        public int TotalSessionCount => _sessionById.Count;

        // ── 内部工具 ──────────────────────────────────────────────────────────

        private bool IsSessionExpired(SessionData sessionData, long nowUnixMs)
        {
            if (sessionData.IsOnline)
                return false;

            return (nowUnixMs - sessionData.LastActiveUnixMs) >= _sessionRetainTimeoutMs;
        }
    }
}