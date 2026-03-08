using System.Security.Cryptography;
using System.Text;
using StellarNet.Client.Network;
using StellarNet.Client.Sender;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Client.GlobalModules.Replay
{
    /// <summary>
    /// 客户端回放模块 Handle，处理回放列表、元信息查询与分块文件下载。
    /// 分块下载状态机由此 Handle 驱动，全部分块到齐后触发 MD5 校验与拼装。
    /// ContentMd5 在元信息阶段由服务端下发并缓存到 Model，分块全部到齐后读取 Model 中缓存的值做校验。
    /// MD5 校验失败时丢弃已下载数据并输出 Error，不将损坏文件写入本地。
    /// 分块请求由客户端按序主动发起，服务端只响应请求，不主动推送分块。
    /// </summary>
    public sealed class ClientReplayHandle
    {
        private readonly ClientReplayModel _model;
        private readonly ClientGlobalMessageRegistrar _registrar;
        private readonly ClientGlobalMessageSender _globalSender;

        // 下载完成事件，参数为完整文件字节数组
        public event System.Action<string, byte[]> OnDownloadCompleted;

        // 下载失败事件
        public event System.Action<string, string> OnDownloadFailed;

        // 下载进度更新事件
        public event System.Action<string, float> OnDownloadProgressUpdated;

        public ClientReplayHandle(
            ClientReplayModel model,
            ClientGlobalMessageRegistrar registrar,
            ClientGlobalMessageSender globalSender)
        {
            if (model == null)
            {
                Debug.LogError("[ClientReplayHandle] 构造失败：model 为 null。");
                return;
            }

            if (registrar == null)
            {
                Debug.LogError("[ClientReplayHandle] 构造失败：registrar 为 null。");
                return;
            }

            if (globalSender == null)
            {
                Debug.LogError("[ClientReplayHandle] 构造失败：globalSender 为 null。");
                return;
            }

            _model = model;
            _registrar = registrar;
            _globalSender = globalSender;
        }

        public void RegisterAll()
        {
            _registrar
                .Register<S2C_ReplayListResult>(OnS2C_ReplayListResult)
                .Register<S2C_ReplayMetaResult>(OnS2C_ReplayMetaResult)
                .Register<S2C_ReplayChunk>(OnS2C_ReplayChunk);
        }

        public void UnregisterAll()
        {
            _registrar
                .Unregister<S2C_ReplayListResult>()
                .Unregister<S2C_ReplayMetaResult>()
                .Unregister<S2C_ReplayChunk>();
        }

        /// <summary>
        /// 请求下载指定回放，先请求元信息，元信息到达后开始分块下载。
        /// </summary>
        public void RequestDownload(string replayId)
        {
            if (string.IsNullOrEmpty(replayId))
            {
                Debug.LogError("[ClientReplayHandle] RequestDownload 失败：replayId 为空。");
                return;
            }

            if (_model.Phase == ClientReplayModel.DownloadPhase.Downloading)
            {
                Debug.LogWarning(
                    $"[ClientReplayHandle] RequestDownload 警告：当前已有下载任务进行中，ReplayId={_model.DownloadingReplayId}，新请求已忽略。");
                return;
            }

            var metaRequest = new C2S_GetReplayMeta { ReplayId = replayId };
            _globalSender.Send(metaRequest);
            Debug.Log($"[ClientReplayHandle] 已请求回放元信息，ReplayId={replayId}。");
        }

        private void OnS2C_ReplayListResult(S2C_ReplayListResult message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientReplayHandle] OnS2C_ReplayListResult 失败：message 为 null。");
                return;
            }

            _model.SetReplayList(message.Replays);
            Debug.Log($"[ClientReplayHandle] 回放列表已更新，总数={message.TotalCount}。");
        }

        private void OnS2C_ReplayMetaResult(S2C_ReplayMetaResult message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientReplayHandle] OnS2C_ReplayMetaResult 失败：message 为 null。");
                return;
            }

            if (!message.Success)
            {
                Debug.LogError($"[ClientReplayHandle] 获取回放元信息失败，ReplayId={message.ReplayId}，原因={message.FailReason}。");
                return;
            }

            if (message.TotalChunks <= 0)
            {
                Debug.LogError(
                    $"[ClientReplayHandle] 回放元信息异常：TotalChunks={message.TotalChunks}，ReplayId={message.ReplayId}。");
                return;
            }

            // ContentMd5 在此处随元信息一并缓存到 Model，分块全部到齐后读取做完整性校验
            // 这是 MD5 的唯一下发时机，不在每个分块中重复携带，减少传输体积
            _model.BeginDownload(message.ReplayId, message.TotalChunks, message.ContentMd5);
            RequestChunk(message.ReplayId, 0);
            Debug.Log(
                $"[ClientReplayHandle] 开始分块下载，ReplayId={message.ReplayId}，TotalChunks={message.TotalChunks}，ContentMd5={message.ContentMd5}。");
        }

        private void OnS2C_ReplayChunk(S2C_ReplayChunk message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientReplayHandle] OnS2C_ReplayChunk 失败：message 为 null。");
                return;
            }

            if (message.ReplayId != _model.DownloadingReplayId)
            {
                Debug.LogWarning($"[ClientReplayHandle] 收到非当前下载任务的分块，收到 ReplayId={message.ReplayId}，" +
                                 $"当前下载 ReplayId={_model.DownloadingReplayId}，已忽略。");
                return;
            }

            if (message.ChunkData == null || message.ChunkData.Length == 0)
            {
                Debug.LogError(
                    $"[ClientReplayHandle] 分块数据为空，ChunkIndex={message.ChunkIndex}，ReplayId={message.ReplayId}。");
                _model.SetDownloadFailed("分块数据为空");
                OnDownloadFailed?.Invoke(message.ReplayId, "分块数据为空");
                return;
            }

            _model.WriteChunk(message.ChunkIndex, message.ChunkData);
            OnDownloadProgressUpdated?.Invoke(message.ReplayId, _model.DownloadProgress);
            if (_model.IsAllChunksReceived)
            {
                // 从 Model 中读取元信息阶段缓存的 ContentMd5，不依赖分块协议携带
                FinalizeDownload(message.ReplayId, _model.CachedContentMd5);
            }
            else
            {
                // 请求下一个分块，客户端按序主动发起
                RequestChunk(message.ReplayId, message.ChunkIndex + 1);
            }
        }

        private void RequestChunk(string replayId, int chunkIndex)
        {
            var chunkRequest = new C2S_RequestReplayChunk
            {
                ReplayId = replayId,
                ChunkIndex = chunkIndex
            };
            _globalSender.Send(chunkRequest);
        }

        /// <summary>
        /// 所有分块到齐后执行 MD5 校验与拼装。
        /// expectedMd5 来自元信息阶段服务端下发并缓存在 Model 中的值，不来自分块协议。
        /// MD5 校验失败时丢弃已下载数据并输出 Error，不将损坏文件写入本地。
        /// </summary>
        private void FinalizeDownload(string replayId, string expectedMd5)
        {
            byte[] assembled = _model.AssembleChunks();
            if (assembled == null)
            {
                Debug.LogError($"[ClientReplayHandle] 分块拼装失败，ReplayId={replayId}。");
                _model.SetDownloadFailed("分块拼装失败");
                OnDownloadFailed?.Invoke(replayId, "分块拼装失败");
                return;
            }

            // MD5 校验，只对帧数据块计算，与服务端 ReplayRecorder 保持一致
            string actualMd5 = ComputeMd5(assembled);
            if (!string.IsNullOrEmpty(expectedMd5) && actualMd5 != expectedMd5)
            {
                Debug.LogError($"[ClientReplayHandle] 回放文件 MD5 校验失败，ReplayId={replayId}，" +
                               $"期望={expectedMd5}，实际={actualMd5}，已丢弃下载数据。");
                _model.SetDownloadFailed("MD5 校验失败，文件可能已损坏");
                OnDownloadFailed?.Invoke(replayId, "MD5 校验失败，文件可能已损坏");
                return;
            }

            _model.SetPhase(ClientReplayModel.DownloadPhase.Completed);
            OnDownloadCompleted?.Invoke(replayId, assembled);
            Debug.Log($"[ClientReplayHandle] 回放文件下载完成，ReplayId={replayId}，文件大小={assembled.Length} 字节，MD5={actualMd5}。");
        }

        private static string ComputeMd5(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(data);
                var sb = new StringBuilder(32);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }
}