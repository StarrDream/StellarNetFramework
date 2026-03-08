using System;
using System.Collections.Generic;
using StellarNet.Shared.Identity;
using UnityEngine;

namespace StellarNet.Server.Session
{
    /// <summary>
    /// 服务端会话管理器，负责会话的创建、查询、连接接管与超时清理。
    /// 任意时刻一个 SessionId 只允许存在一个有效主连接。
    /// 新连接完成重连认证成功后，旧连接必须立即标记为 Replaced，其后续来包一律拒收。
    /// Session 保留超时与 Room 空置销毁超时独立配置、独立计时、独立生效。
    /// </summary>
    public sealed class SessionManager
    {
        // SessionId → SessionRecord 主索引
        private readonly Dictionary<string, SessionRecord> _sessionById
            = new Dictionary<string, SessionRecord>();

        // ConnectionId → SessionId 反向索引，用于快速通过连接查找会话
        private readonly Dictionary<int, string> _sessionIdByConnectionValue
            = new Dictionary<int, string>();

        // Session 保留超时时长（秒），与 Room 空置超时独立
        private float _sessionRetainTimeoutSeconds;

        // 上次巡检时间，用于控制巡检频率
        private float _lastCleanupTime;
        private const float CleanupIntervalSeconds = 30f;

        public SessionManager(float sessionRetainTimeoutSeconds)
        {
            _sessionRetainTimeoutSeconds = sessionRetainTimeoutSeconds;
            _lastCleanupTime = UnityEngine.Time.realtimeSinceStartup;
        }

        /// <summary>
        /// 更新 Session 保留超时配置，由 NetConfigManager 热重载后调用。
        /// 只对后续新判定生效，不追溯已在计时中的会话。
        /// </summary>
        public void UpdateRetainTimeout(float timeoutSeconds)
        {
            _sessionRetainTimeoutSeconds = timeoutSeconds;
        }

        /// <summary>
        /// 创建新会话，在登录成功时调用。
        /// 生成服务端签发的 SessionId，不等价于账号 ID 或客户端本地生成 ID。
        /// </summary>
        public SessionRecord CreateSession(ConnectionId connectionId)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError($"[SessionManager] CreateSession 失败：ConnectionId 无效，当前值：{connectionId}。");
                return null;
            }

            // 生成唯一 SessionId：时间戳 + 随机码，保证唯一性与可诊断性
            string sessionId = GenerateSessionId();
            var record = new SessionRecord(sessionId, connectionId);

            _sessionById[sessionId] = record;
            _sessionIdByConnectionValue[connectionId.Value] = sessionId;

            Debug.Log($"[SessionManager] 新会话创建成功，SessionId={sessionId}，ConnectionId={connectionId}。");
            return record;
        }

        /// <summary>
        /// 通过 SessionId 查询会话记录。
        /// 查询失败返回 null，调用方必须做判空处理。
        /// </summary>
        public SessionRecord GetSessionById(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return null;
            }

            _sessionById.TryGetValue(sessionId, out var record);
            return record;
        }

        /// <summary>
        /// 通过 ConnectionId 查询会话记录。
        /// 查询失败返回 null，调用方必须做判空处理。
        /// </summary>
        public SessionRecord GetSessionByConnectionId(ConnectionId connectionId)
        {
            if (!connectionId.IsValid)
            {
                return null;
            }

            if (!_sessionIdByConnectionValue.TryGetValue(connectionId.Value, out var sessionId))
            {
                return null;
            }

            _sessionById.TryGetValue(sessionId, out var record);
            return record;
        }

        /// <summary>
        /// 处理底层连接断开事件，将对应会话的 ConnectionId 标记为 Invalid。
        /// SessionId 仍保留，用于后续重连恢复。
        /// </summary>
        public void OnConnectionDisconnected(ConnectionId connectionId)
        {
            if (!connectionId.IsValid)
            {
                return;
            }

            if (!_sessionIdByConnectionValue.TryGetValue(connectionId.Value, out var sessionId))
            {
                // 连接断开但找不到对应会话，可能是未完成认证的连接，属于正常情况
                Debug.Log($"[SessionManager] 连接断开但未找到对应会话，ConnectionId={connectionId}，可能是未完成认证的连接。");
                return;
            }

            _sessionIdByConnectionValue.Remove(connectionId.Value);

            if (_sessionById.TryGetValue(sessionId, out var record))
            {
                record.MarkDisconnected();
                Debug.Log($"[SessionManager] 会话标记为断线，SessionId={sessionId}，ConnectionId={connectionId}。");
            }
        }

        /// <summary>
        /// 执行重连接管：将原会话绑定到新连接，旧连接标记为 Replaced。
        /// 任意时刻一个 SessionId 只允许存在一个有效主连接。
        /// 若会话当前位于房间内，调用方（ReconnectHandle）必须先完成房间连接映射替换，再调用此方法。
        /// </summary>
        public bool TakeoverSession(string sessionId, ConnectionId newConnectionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError($"[SessionManager] TakeoverSession 失败：sessionId 为空，新 ConnectionId={newConnectionId}。");
                return false;
            }

            if (!newConnectionId.IsValid)
            {
                Debug.LogError($"[SessionManager] TakeoverSession 失败：新 ConnectionId 无效，SessionId={sessionId}。");
                return false;
            }

            if (!_sessionById.TryGetValue(sessionId, out var record))
            {
                Debug.LogError($"[SessionManager] TakeoverSession 失败：SessionId={sessionId} 不存在，无法执行接管。");
                return false;
            }

            // 清除旧连接的反向索引
            if (record.CurrentConnectionId.IsValid)
            {
                _sessionIdByConnectionValue.Remove(record.CurrentConnectionId.Value);
                Debug.Log(
                    $"[SessionManager] 旧连接接管：SessionId={sessionId}，旧 ConnectionId={record.CurrentConnectionId} 已标记为 Replaced。");
            }

            // 建立新连接的反向索引
            _sessionIdByConnectionValue[newConnectionId.Value] = sessionId;
            record.UpdateConnection(newConnectionId);

            Debug.Log($"[SessionManager] 会话接管成功，SessionId={sessionId}，新 ConnectionId={newConnectionId}。");
            return true;
        }

        /// <summary>
        /// 销毁指定会话，在会话超时或主动登出时调用。
        /// </summary>
        public void DestroySession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            if (!_sessionById.TryGetValue(sessionId, out var record))
            {
                return;
            }

            if (record.CurrentConnectionId.IsValid)
            {
                _sessionIdByConnectionValue.Remove(record.CurrentConnectionId.Value);
            }

            _sessionById.Remove(sessionId);
            Debug.Log($"[SessionManager] 会话已销毁，SessionId={sessionId}。");
        }

        /// <summary>
        /// 主循环 Tick，执行超时会话巡检与清理。
        /// 由 GlobalInfrastructure.Tick() 驱动，不使用独立线程。
        /// </summary>
        public void Tick()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastCleanupTime < CleanupIntervalSeconds)
            {
                return;
            }

            _lastCleanupTime = now;
            CleanupExpiredSessions();
        }

        /// <summary>
        /// 清理超时的离线会话。
        /// 只清理已断线（ConnectionId 无效）且超过保留时长的会话。
        /// 在线会话不受此清理影响。
        /// </summary>
        private void CleanupExpiredSessions()
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long timeoutMs = (long)(_sessionRetainTimeoutSeconds * 1000f);

            var expiredIds = new List<string>();
            foreach (var kv in _sessionById)
            {
                var record = kv.Value;
                // 只清理已断线的会话，在线会话不受影响
                if (!record.IsOnline && (nowMs - record.LastActiveUnixMs) > timeoutMs)
                {
                    expiredIds.Add(kv.Key);
                }
            }

            foreach (var id in expiredIds)
            {
                Debug.Log($"[SessionManager] 会话超时清理，SessionId={id}。");
                DestroySession(id);
            }
        }

        /// <summary>
        /// 生成唯一 SessionId，采用时间戳 + 随机码组合，保证唯一性与可诊断性。
        /// </summary>
        private static string GenerateSessionId()
        {
            long unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string randomPart = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            return $"S_{unixMs}_{randomPart}";
        }

        /// <summary>
        /// 获取当前所有在线会话的 ConnectionId 列表，用于全局广播目标集合计算。
        /// 只返回 IsOnline=true 的会话连接，离线会话不包含在内。
        /// 返回快照副本，不返回内部集合引用。
        /// </summary>
        public List<StellarNet.Shared.Identity.ConnectionId> GetAllOnlineConnectionIds()
        {
            var result = new List<StellarNet.Shared.Identity.ConnectionId>();
            foreach (var kv in _sessionById)
            {
                if (kv.Value.IsOnline)
                {
                    result.Add(kv.Value.CurrentConnectionId);
                }
            }

            return result;
        }
    }
}