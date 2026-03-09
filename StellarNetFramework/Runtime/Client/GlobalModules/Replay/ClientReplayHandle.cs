using System.Security.Cryptography;
using System.Text;
using StellarNet.Client.Network;
using StellarNet.Client.Sender;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Client.GlobalModules.Replay
{
    public sealed class ClientReplayHandle
    {
        private readonly ClientReplayModel _model;
        public ClientReplayModel Model => _model;

        private readonly ClientGlobalMessageRegistrar _registrar;
        private readonly ClientGlobalMessageSender _globalSender;

        public event System.Action<string, byte[]> OnDownloadCompleted;
        public event System.Action<string, string> OnDownloadFailed;
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

        public void RequestDownload(string replayId)
        {
            if (string.IsNullOrEmpty(replayId))
            {
                Debug.LogError("[ClientReplayHandle] RequestDownload 失败：replayId 为空。");
                return;
            }

            if (_model.Phase == ClientReplayModel.DownloadPhase.Downloading)
            {
                Debug.LogWarning($"[ClientReplayHandle] RequestDownload 警告：当前已有下载任务进行中，ReplayId={_model.DownloadingReplayId}，新请求已忽略。");
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
                Debug.LogError($"[ClientReplayHandle] 回放元信息异常：TotalChunks={message.TotalChunks}，ReplayId={message.ReplayId}。");
                return;
            }

            _model.BeginDownload(message.ReplayId, message.TotalChunks, message.ContentMd5);
            RequestChunk(message.ReplayId, 0);

            Debug.Log($"[ClientReplayHandle] 开始分块下载，ReplayId={message.ReplayId}，TotalChunks={message.TotalChunks}，ContentMd5={message.ContentMd5}。");
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
                Debug.LogWarning($"[ClientReplayHandle] 收到非当前下载任务的分块，收到 ReplayId={message.ReplayId}，当前下载 ReplayId={_model.DownloadingReplayId}，已忽略。");
                return;
            }

            if (message.ChunkData == null || message.ChunkData.Length == 0)
            {
                Debug.LogError($"[ClientReplayHandle] 分块数据为空，ChunkIndex={message.ChunkIndex}，ReplayId={message.ReplayId}。");
                _model.SetDownloadFailed("分块数据为空");
                OnDownloadFailed?.Invoke(message.ReplayId, "分块数据为空");
                return;
            }

            _model.WriteChunk(message.ChunkIndex, message.ChunkData);
            OnDownloadProgressUpdated?.Invoke(message.ReplayId, _model.DownloadProgress);

            if (_model.IsAllChunksReceived)
            {
                FinalizeDownload(message.ReplayId, _model.CachedContentMd5);
            }
            else
            {
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

            byte[] separator = Encoding.UTF8.GetBytes("\n---FRAMES---\n");
            int separatorIndex = IndexOfByteArray(assembled, separator);

            if (separatorIndex == -1)
            {
                Debug.LogError($"[ClientReplayHandle] 回放文件格式错误：未找到数据分隔符，ReplayId={replayId}。");
                _model.SetDownloadFailed("文件格式损坏");
                OnDownloadFailed?.Invoke(replayId, "文件格式损坏");
                return;
            }

            int framesStartIndex = separatorIndex + separator.Length;
            int framesLength = assembled.Length - framesStartIndex;
            byte[] framesData = new byte[framesLength];
            System.Buffer.BlockCopy(assembled, framesStartIndex, framesData, 0, framesLength);

            string actualMd5 = ComputeMd5(framesData);

            if (!string.IsNullOrEmpty(expectedMd5) && actualMd5 != expectedMd5)
            {
                Debug.LogError($"[ClientReplayHandle] 回放文件 MD5 校验失败，ReplayId={replayId}，期望={expectedMd5}，实际={actualMd5}，已丢弃下载数据。");
                _model.SetDownloadFailed("MD5 校验失败，文件可能已损坏");
                OnDownloadFailed?.Invoke(replayId, "MD5 校验失败，文件可能已损坏");
                return;
            }

            _model.SetPhase(ClientReplayModel.DownloadPhase.Completed);
            OnDownloadCompleted?.Invoke(replayId, assembled);

            Debug.Log($"[ClientReplayHandle] 回放文件下载完成，ReplayId={replayId}，文件大小={assembled.Length} 字节，MD5={actualMd5}。");
        }

        private static int IndexOfByteArray(byte[] source, byte[] pattern)
        {
            if (source == null || pattern == null || source.Length == 0 || pattern.Length == 0 || pattern.Length > source.Length)
            {
                return -1;
            }

            for (int i = 0; i <= source.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
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