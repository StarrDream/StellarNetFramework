using System;
using System.Collections.Generic;
using StellarNet.Shared.Protocol.BuiltIn;
using StellarNet.Shared.Registry;
using UnityEngine;

namespace StellarNet.Client.State
{
    /// <summary>
    /// 客户端状态协议过滤器，位于 ClientNetworkEntry 与 Router 之间。
    /// 根据当前客户端主状态（ClientAppState），严格拦截非法协议与非法状态迁移。
    /// 拦截失败时直接输出 Error 并阻断协议继续向下派发。
    /// </summary>
    public sealed class ClientStateProtocolFilter
    {
        private readonly IClientStateProvider _stateProvider;

        // 回放模式下允许接收的全局域协议白名单
        private readonly HashSet<Type> _replayGlobalWhitelist;

        public ClientStateProtocolFilter(IClientStateProvider stateProvider)
        {
            if (stateProvider == null)
            {
                Debug.LogError("[ClientStateProtocolFilter] 构造失败：stateProvider 为 null。");
                return;
            }

            _stateProvider = stateProvider;

            // 初始化回放模式全局协议白名单
            _replayGlobalWhitelist = new HashSet<Type>
            {
                typeof(S2C_KickOut),
                typeof(S2C_AnnouncementPush),
                typeof(S2C_LobbyChatMessage)
            };
        }

        /// <summary>
        /// 校验当前状态下是否允许接收该下行协议。
        /// </summary>
        public bool CanReceive(MessageMetadata metadata)
        {
            if (metadata == null)
            {
                Debug.LogError("[ClientStateProtocolFilter] CanReceive 失败：metadata 为 null。");
                return false;
            }

            ClientAppState currentState = _stateProvider.CurrentState;
            Type msgType = metadata.MessageType;

            switch (currentState)
            {
                case ClientAppState.Disconnected:
                    // 断线状态下不处理任何业务协议
                    Debug.LogWarning($"[ClientStateProtocolFilter] 拦截：当前处于 Disconnected 状态，拒绝接收协议 {msgType?.Name}。");
                    return false;

                case ClientAppState.Authenticating:
                    // 认证中只允许接收登录结果、重连结果与踢下线通知
                    if (msgType == typeof(S2C_LoginResult) ||
                        msgType == typeof(S2C_ReconnectResult) ||
                        msgType == typeof(S2C_KickOut))
                    {
                        return true;
                    }

                    Debug.LogWarning($"[ClientStateProtocolFilter] 拦截：当前处于 Authenticating 状态，拒绝接收协议 {msgType?.Name}。");
                    return false;

                case ClientAppState.InLobby:
                    // 大厅状态下拒绝所有房间域协议，防止延迟消息污染
                    if (metadata.Domain == MessageDomain.Room)
                    {
                        Debug.LogWarning($"[ClientStateProtocolFilter] 拦截：当前处于 InLobby 状态，拒绝接收房间域协议 {msgType?.Name}。");
                        return false;
                    }

                    return true;

                case ClientAppState.InRoom:
                    // 房间状态下，拒绝再次收到登录或重连结果，防止状态机错乱
                    if (msgType == typeof(S2C_LoginResult) || msgType == typeof(S2C_ReconnectResult))
                    {
                        Debug.LogError(
                            $"[ClientStateProtocolFilter] 非法迁移拦截：当前处于 InRoom 状态，收到非法协议 {msgType?.Name}，已阻断。");
                        return false;
                    }

                    return true;

                case ClientAppState.InReplay:
                    // 回放状态下严格隔离在线房间域消息
                    if (metadata.Domain == MessageDomain.Room)
                    {
                        Debug.LogWarning(
                            $"[ClientStateProtocolFilter] 拦截：当前处于 InReplay 状态，拒绝接收在线房间域协议 {msgType?.Name}。");
                        return false;
                    }

                    // 全局域消息必须在白名单内
                    if (!_replayGlobalWhitelist.Contains(msgType))
                    {
                        Debug.LogWarning(
                            $"[ClientStateProtocolFilter] 拦截：当前处于 InReplay 状态，全局域协议 {msgType?.Name} 不在白名单内。");
                        return false;
                    }

                    return true;

                default:
                    Debug.LogError($"[ClientStateProtocolFilter] 未知状态 {currentState}，默认拦截协议 {msgType?.Name}。");
                    return false;
            }
        }
    }
}