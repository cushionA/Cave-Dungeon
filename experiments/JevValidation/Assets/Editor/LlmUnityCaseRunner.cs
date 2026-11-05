using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UndreamAI.LlamaLib;

namespace JevValidation
{
    public static class LlmUnityCaseRunner
    {
        private static readonly SemaphoreSlim Residency = new SemaphoreSlim(1, 1);
        private const int RequestTimeoutSeconds = 120;

        public static async Task<ValidationReport> RunAsync(string modelPath, string casePath,
            CancellationToken cancellationToken = default, LlmUnityBackend backend = LlmUnityBackend.CPU, int gpuLayers = 99)
        {
            var suite = PreparedCaseFile.Read(casePath);
            var headPath = GgufInputs.ResolveHeadPath(casePath, suite);
            modelPath = Path.GetFullPath(modelPath);
            if (!string.Equals(Path.GetExtension(modelPath), ".gguf", StringComparison.OrdinalIgnoreCase) ||
                modelPath.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
                throw new InvalidDataException("GGUFファイルを選択してください。");
            var command = LlmUnityGpuSettings.BuildCommand(modelPath, backend, gpuLayers, suite.token_count);
            foreach (var item in suite.cases) GgufInputs.ActiveTokenIds(item);
            await Residency.WaitAsync(cancellationToken);
            LLMService service = null;
            var gpuEvidence = new GpuOffloadEvidence();
            LlamaLib.CharArrayCallback gpuLogCallback = gpuEvidence.Capture;
            var previousExclusions = LlamaLib.libraryExclusion;
            try
            {
                if (backend == LlmUnityBackend.GPU)
                    LlamaLib.libraryExclusion = new List<string> { "tinyblas", "vulkan" };
                var head = await Task.Run(() =>
                {
                    GgufInputs.VerifyHash(modelPath, suite.model_sha256);
                    return JevPointerHead.Load(headPath, suite.head_sha256);
                });
                cancellationToken.ThrowIfCancellationRequested();
                if (LLMUnity.LLMUnitySetup.libraryProgress < 1)
                    throw new InvalidOperationException("LLMUnityのネイティブライブラリ準備完了を待ってください。");
                var timer = Stopwatch.StartNew();
                service = await Task.Run(() =>
                {
                    // FromCommand reads a delegate initialized by the first LlamaLib constructor.
                    var initializer = new LlamaLib(backend == LlmUnityBackend.GPU);
                    try
                    {
                        if (backend == LlmUnityBackend.GPU)
                        {
                            LlamaLib.Debug(5);
                            LlamaLib.LoggingCallback(gpuLogCallback);
                        }
                        return LLMService.FromCommand(command);
                    }
                    catch
                    {
                        ResetLogging();
                        throw;
                    }
                    finally { initializer.Dispose(); }
                });
                cancellationToken.ThrowIfCancellationRequested();
                var listener = new TcpListener(IPAddress.Loopback, 0);
                int port;
                try
                {
                    listener.Start();
                    port = ((IPEndPoint)listener.LocalEndpoint).Port;
                }
                finally { listener.Stop(); }
                service.StartServer("127.0.0.1", port);
                if (!await service.StartAsync()) throw new InvalidOperationException("LLMUnityサービスを開始できません。");
                if (backend == LlmUnityBackend.GPU)
                {
                    if (service.llamaLib.LLMService_Supports_GPU())
                        for (var attempt = 0; attempt < 60 && !gpuEvidence.Verified; attempt++)
                            await Task.Delay(50, cancellationToken);
                    if (!service.llamaLib.LLMService_Supports_GPU() || !gpuEvidence.Verified)
                        throw new InvalidOperationException("GPUへの実offloadを確認できません。CPU fallbackをGPU成功として扱いません。\n" + string.Join("\n", gpuEvidence.Lines));
                }
                timer.Stop();
                var loadMilliseconds = timer.Elapsed.TotalMilliseconds;
                if (service.EmbeddingSize() != JevPointerHead.HiddenSize)
                    throw new InvalidDataException("GGUFモデルのhidden sizeが1024ではありません。");
                using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
                {
                    BaseAddress = new Uri("http://127.0.0.1:" + port),
                    Timeout = Timeout.InfiniteTimeSpan
                };
                await InferAsync(client, head, suite.cases[0], cancellationToken);
                var probabilities = new float[suite.cases.Length][];
                var milliseconds = new double[suite.cases.Length];
                var backboneMilliseconds = new double[suite.cases.Length];
                var headMilliseconds = new double[suite.cases.Length];
                for (var index = 0; index < suite.cases.Length; index++)
                {
                    var result = await InferAsync(client, head, suite.cases[index], cancellationToken);
                    probabilities[index] = result.Probabilities;
                    milliseconds[index] = result.TotalMilliseconds;
                    backboneMilliseconds[index] = result.BackboneMilliseconds;
                    headMilliseconds[index] = result.HeadMilliseconds;
                }
                var report = ReportBuilder.Build(suite, "LLMUnity 3.0.3 / LlamaLib 2.0.5 " + backend, modelPath, probabilities, milliseconds);
                report.model_format = "gguf";
                report.head_sha256 = suite.head_sha256;
                report.backbone_quantization = suite.backbone_quantization;
                report.model_load_ms = loadMilliseconds;
                var gpuSnapshot = gpuEvidence.Read();
                report.requested_gpu_layers = backend == LlmUnityBackend.GPU ? gpuLayers : 0;
                report.offloaded_gpu_layers = gpuSnapshot.OffloadedLayers;
                report.offloadable_gpu_layers = gpuSnapshot.TotalLayers;
                report.gpu_offload_verified = gpuEvidence.Verified;
                report.gpu_model_buffer_mib = gpuSnapshot.ModelBufferMib;
                report.native_architecture = service.llamaLib.architecture;
                report.native_gpu_log = gpuSnapshot.Lines;
                report.timing_scope = "HTTP request through all-token readback, JSON validation and original Jev head; I/O continuations use worker threads. Model load and one warm-up excluded. CPU threads=4; GPU layer request=" + report.requested_gpu_layers + ".";
                for (var index = 0; index < report.cases.Length; index++)
                {
                    report.cases[index].backbone_ms = backboneMilliseconds[index];
                    report.cases[index].head_ms = headMilliseconds[index];
                }
                return report;
            }
            finally
            {
                try
                {
                    if (service != null)
                        await Task.Run(() =>
                        {
                            try { service.StopServer(); }
                            finally
                            {
                                try { service.Stop(); }
                                finally
                                {
                                    try { ResetLogging(); }
                                    finally { service.Dispose(); }
                                }
                            }
                        });
                }
                finally
                {
                    try { ResetLogging(); }
                    finally
                    {
                        try { LlamaLib.libraryExclusion = previousExclusions; }
                        finally
                        {
                            GC.KeepAlive(gpuLogCallback);
                            Residency.Release();
                        }
                    }
                }
            }
        }

        private static void ResetLogging()
        {
            try { LlamaLib.LoggingStop(); }
            finally { LlamaLib.Debug(0); }
        }

        private static async Task<InferenceResult> InferAsync(HttpClient client, JevPointerHead head,
            PreparedCase item, CancellationToken cancellationToken)
        {
            var tokens = GgufInputs.ActiveTokenIds(item);
            using var content = new StringContent(JsonConvert.SerializeObject(new { content = tokens }), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/embeddings") { Content = content };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(RequestTimeoutSeconds));
            try
            {
                var timer = Stopwatch.StartNew();
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > GgufInputs.MaximumResponseCharacters)
                    throw new InvalidDataException("Embedding応答が上限を超えています。");
                using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) != 0)
                {
                    if (output.Length + read > GgufInputs.MaximumResponseCharacters)
                        throw new InvalidDataException("Embedding応答が上限を超えています。");
                    output.Write(buffer, 0, read);
                }
                var hidden = GgufInputs.ParseEmbeddings(Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length), tokens.Length);
                var backboneMilliseconds = timer.Elapsed.TotalMilliseconds;
                var probabilities = head.Predict(hidden, item);
                var elapsed = timer.Elapsed.TotalMilliseconds;
                cancellationToken.ThrowIfCancellationRequested();
                return new InferenceResult(probabilities, elapsed, backboneMilliseconds, elapsed - backboneMilliseconds);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Embedding応答が120秒以内に完了しませんでした。", exception);
            }
        }

        private sealed class InferenceResult
        {
            public readonly float[] Probabilities;
            public readonly double TotalMilliseconds;
            public readonly double BackboneMilliseconds;
            public readonly double HeadMilliseconds;

            public InferenceResult(float[] probabilities, double total, double backbone, double head)
            {
                Probabilities = probabilities;
                TotalMilliseconds = total;
                BackboneMilliseconds = backbone;
                HeadMilliseconds = head;
            }
        }
    }
}
