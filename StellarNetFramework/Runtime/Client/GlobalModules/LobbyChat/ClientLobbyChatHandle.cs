using StellarNet.Client.Network;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Client.GlobalModules.LobbyChat
{
    /// <summary>
    /// 客户端大厅聊天模块 Handle，处理聊天消息接收、历史消息同步与发言拦截通知。
    /// 收到 S2C_LobbyChatMessage 时追加到本地历史缓冲并触发 OnMessageReceived 事件。
    /// 收到 S2C_LobbyChatBlocked 时写入冷却状态并触发 OnChatBlocked 事件，供 View 层展示。
    /// Handle 不直接操作 View，通过 Model 状态变更与事件驱动 View 刷新（MSV 架构）。
    /// </summary>
    public sealed class ClientLobbyChatHandle
    {
        private readonly ClientLobbyChatModel _model;

        public ClientLobbyChatModel Model => _model;

        private readonly ClientGlobalMessageRegistrar _registrar;

        // 收到新聊天消息事件，供 View 层订阅
        public event System.Action<LobbyChatHistoryItem> OnMessageReceived;

        // 发言被拦截事件，供 View 层展示拦截原因
        public event System.Action<string> OnChatBlocked;

        // 历史消息同步完成事件，供 View 层刷新历史消息列表
        public event System.Action OnHistorySynced;

        public ClientLobbyChatHandle(
            ClientLobbyChatModel model,
            ClientGlobalMessageRegistrar registrar)
        {
            if (model == null)
            {
                Debug.LogError("[ClientLobbyChatHandle] 构造失败：model 为 null。");
                return;
            }

            if (registrar == null)
            {
                Debug.LogError("[ClientLobbyChatHandle] 构造失败：registrar 为 null。");
                return;
            }

            _model = model;
            _registrar = registrar;
        }

        public void RegisterAll()
        {
            _registrar
                .Register<S2C_LobbyChatMessage>(OnS2C_LobbyChatMessage)
                .Register<S2C_LobbyChatBlocked>(OnS2C_LobbyChatBlocked)
                .Register<S2C_LobbyChatHistoryResult>(OnS2C_LobbyChatHistoryResult);
        }

        public void UnregisterAll()
        {
            _registrar
                .Unregister<S2C_LobbyChatMessage>()
                .Unregister<S2C_LobbyChatBlocked>()
                .Unregister<S2C_LobbyChatHistoryResult>();
        }

        private void OnS2C_LobbyChatMessage(S2C_LobbyChatMessage message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientLobbyChatHandle] OnS2C_LobbyChatMessage 失败：message 为 null。");
                return;
            }

            var item = new LobbyChatHistoryItem
            {
                SenderSessionId = message.SenderSessionId,
                Content = message.Content,
                SendUnixMs = message.SendUnixMs,
                MessageType = message.MessageType
            };

            _model.AppendMessage(item);
            OnMessageReceived?.Invoke(item);
        }

        private void OnS2C_LobbyChatBlocked(S2C_LobbyChatBlocked message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientLobbyChatHandle] OnS2C_LobbyChatBlocked 失败：message 为 null。");
                return;
            }

            long nowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 本地冷却预判写入 Model，具体冷却时长以服务端为准
            // 此处设置 1 秒本地预判冷却，仅用于 UI 体验优化，不作为最终发言权限依据
            _model.SetCooldown(nowMs + 1000L);
            _model.SetBlockReason(message.Reason);
            OnChatBlocked?.Invoke(message.Reason);

            Debug.LogWarning($"[ClientLobbyChatHandle] 发言被服务端拦截，原因={message.Reason}。");
        }

        private void OnS2C_LobbyChatHistoryResult(S2C_LobbyChatHistoryResult message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientLobbyChatHandle] OnS2C_LobbyChatHistoryResult 失败：message 为 null。");
                return;
            }

            _model.SetHistory(message.Messages);
            OnHistorySynced?.Invoke();

            Debug.Log($"[ClientLobbyChatHandle] 大厅聊天历史消息同步完成，消息数量={message.Messages?.Length ?? 0}。");
        }
    }
}