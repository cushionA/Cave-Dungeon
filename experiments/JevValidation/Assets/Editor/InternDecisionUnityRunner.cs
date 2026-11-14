using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UndreamAI.LlamaLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace JevValidation
{
    [Serializable]
    public sealed class InternDecisionUnityManifest
    {
        public int schema_version;
        public string model_id;
        public string revision;
        public string gguf_file;
        public string gguf_sha256;
        public string head_file;
        public string head_sha256;
        public int hidden_size;
        public int context_size;
        public string reference_result_file;
        public string reference_result_sha256;
        public string inputs_sha256;
        public InternDecisionUnityProtocol decision_protocol;
        public InternDecisionUnityCase[] cases;
    }

    [Serializable]
    public sealed class InternDecisionUnityProtocol
    {
        public double temperature;
        public int decision_token_id;
        public bool padding;
        public bool truncation;
        public bool apply_chat_template_in_unity;
    }

    [Serializable]
    public sealed class InternDecisionUnityCase
    {
        public string case_id;
        public string request_sha256;
        public int[] input_ids;
        public int decision_position;
        public string[] choice_ids;
        public float[] reference_probabilities;
        public int[] symbol_token_ids;
    }

    public sealed class InternDecisionLmHead
    {
        public int hidden_size;
        public float[][] rows;
        public int[] symbol_token_ids;
    }

    public sealed class InternDecisionCompletionOutput
    {
        public float[] probabilities;
        public int tokens_evaluated;
        public int tokens_predicted;
        public double prompt_ms;
        public double predicted_ms;
    }

    [Serializable]
    public sealed class InternDecisionUnityCaseResult
    {
        public string case_id;
        public int input_token_count;
        public int processed_input_tokens;
        public string route;
        public string probability_source;
        public bool raw_logits_available;
        public int decision_position;
        public string[] choice_ids;
        public int[] symbol_token_ids;
        public double[] logits;
        public float[] probabilities;
        public float[] reference_probabilities;
        public string chosen_id;
        public string reference_chosen_id;
        public bool selected_reference_matches;
        public float max_reference_error;
        public double inference_ms;
        public double backbone_ms;
        public double head_ms;
        public bool has_native_timings;
        public double native_prompt_ms;
        public double native_predicted_ms;
        public int native_tokens_predicted;
    }

    [Serializable]
    public sealed class InternDecisionUnityReport
    {
        public int schema_version = 1;
        public string created_utc = DateTime.UtcNow.ToString("O");
        public string unity_version = Application.unityVersion;
        public string processor = SystemInfo.processorType;
        public string operating_system = SystemInfo.operatingSystem;
        public string model_id;
        public string revision;
        public string manifest_path;
        public string manifest_sha256;
        public string gguf_sha256;
        public string head_sha256;
        public string reference_result_sha256;
        public bool reference_result_verified;
        public bool decision_protocol_verified;
        public int context_size;
        public int hidden_size;
        public int cpu_threads = 4;
        public int requested_gpu_layers;
        public string native_architecture;
        public string route = "embeddings";
        public int processed_input_tokens;
        public int warmup_processed_input_tokens;
        public int completion_vocabulary_size;
        public int completion_logit_bias_entries;
        public string backend = "LLMUnity 3.0.3 / LlamaLib 2.0.5 CPU, original Intern-Decision legal LM-head rows";
        public string command;
        public string stage;
        public string error;
        public bool success;
        public bool execution_completed;
        public bool diagnostic_parity_within_tolerance;
        public float diagnostic_absolute_tolerance = 0.02f;
        public string parity_scope = "Agreement and probability difference against pinned original HF results. The 0.02 threshold is a numerical diagnostic, not decision quality or game success.";
        public string timing_scope = "HTTP request through all-token embedding JSON validation and original legal-token LM head. Model startup, file hashing and one separately recorded warm-up are excluded.";
        public double validation_ms;
        public double model_load_ms;
        public double warmup_ms;
        public double mean_inference_ms;
        public double mean_backbone_ms;
        public double mean_head_ms;
        public int executed_cases;
        public int selected_reference_matches;
        public float max_reference_error;
        public string[] native_log;
        public InternDecisionUnityCaseResult[] cases;

        public void Save(string path)
        {
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, JsonUtility.ToJson(this, true));
        }
    }

    public static class InternDecisionUnityRunner
    {
        private const int ChoiceCount = 8;
        private const int CompletionVocabularySize = 248320;
        private const string CompletionModelId = "internlm/Intern-Decision-0.8B";
        private const string CompletionModelRevision = "42457fafd13d10d0b0d9be46cb30a23caa409994";
        private const int MaximumResponseBytes = 256 * 1024 * 1024;
        private static readonly SemaphoreSlim Residency = new SemaphoreSlim(1, 1);
        private static readonly Dictionary<string, string> CompletionBiasCache = new Dictionary<string, string>(StringComparer.Ordinal);

        public static void ValidateManifest(InternDecisionUnityManifest manifest)
        {
            if (manifest == null || manifest.schema_version != 1 || string.IsNullOrWhiteSpace(manifest.model_id) ||
                string.IsNullOrWhiteSpace(manifest.revision) || manifest.hidden_size != 1024 ||
                manifest.context_size < 1 || manifest.context_size > 4096 || manifest.decision_protocol == null ||
                !IsHash(manifest.gguf_sha256) || !IsHash(manifest.head_sha256) || !IsHash(manifest.reference_result_sha256) ||
                string.IsNullOrWhiteSpace(manifest.gguf_file) || string.IsNullOrWhiteSpace(manifest.head_file) ||
                string.IsNullOrWhiteSpace(manifest.reference_result_file) || manifest.cases == null || manifest.cases.Length == 0)
                throw new InvalidDataException("Intern-Decision manifest identity, artifacts or context contract is invalid.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in manifest.cases)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.case_id) || !ids.Add(item.case_id) ||
                    item.input_ids == null || item.input_ids.Length < 2 || item.input_ids.Length > manifest.context_size ||
                    Array.Exists(item.input_ids, id => id < 0) || item.decision_position < 0 ||
                    item.decision_position >= item.input_ids.Length - 1 ||
                    item.choice_ids == null || item.choice_ids.Length != ChoiceCount ||
                    Array.Exists(item.choice_ids, string.IsNullOrWhiteSpace) ||
                    new HashSet<string>(item.choice_ids, StringComparer.Ordinal).Count != ChoiceCount ||
                    item.symbol_token_ids == null || item.symbol_token_ids.Length != ChoiceCount ||
                    Array.Exists(item.symbol_token_ids, id => id < 0) ||
                    new HashSet<int>(item.symbol_token_ids).Count != ChoiceCount)
                    throw new InvalidDataException("Intern-Decision case requires unpadded original IDs, a pre-marker decision position and eight distinct legal symbols/choices.");
                PreparedCaseFile.ValidateProbabilities(item.reference_probabilities, ChoiceCount);
                if (manifest.decision_protocol != null)
                {
                    var protocol = manifest.decision_protocol;
                    if (protocol.temperature != 1 || protocol.decision_token_id < 0 || protocol.padding || protocol.truncation ||
                        protocol.apply_chat_template_in_unity || item.input_ids[item.decision_position + 1] != protocol.decision_token_id ||
                        Array.FindAll(item.input_ids, id => id == protocol.decision_token_id).Length != 1)
                        throw new InvalidDataException("Original decision marker, temperature or no-padding/template protocol changed.");
                }
            }
        }

        public static void ValidateHead(InternDecisionLmHead head)
        {
            if (head == null || head.hidden_size != 1024 || head.rows == null || head.rows.Length != ChoiceCount ||
                head.symbol_token_ids == null || head.symbol_token_ids.Length != ChoiceCount ||
                Array.Exists(head.symbol_token_ids, id => id < 0) || new HashSet<int>(head.symbol_token_ids).Count != ChoiceCount)
                throw new InvalidDataException("Intern-Decision head must hold eight original legal-token rows of width 1024.");
            foreach (var row in head.rows)
                if (row == null || row.Length != head.hidden_size || Array.Exists(row, value => !GgufInputs.IsFinite(value)))
                    throw new InvalidDataException("Intern-Decision head row has an invalid width or nonfinite weight.");
        }

        public static float[] Predict(float[] decisionHidden, InternDecisionLmHead head, int[] symbolTokenIds, out double[] logits)
        {
            ValidateHead(head);
            if (decisionHidden == null || decisionHidden.Length != head.hidden_size ||
                Array.Exists(decisionHidden, value => !GgufInputs.IsFinite(value)) ||
                symbolTokenIds == null || symbolTokenIds.Length != ChoiceCount ||
                new HashSet<int>(symbolTokenIds).Count != ChoiceCount)
                throw new InvalidDataException("Decision hidden state or legal-symbol order is invalid.");
            logits = new double[ChoiceCount];
            for (var choice = 0; choice < ChoiceCount; choice++)
            {
                var rowIndex = Array.IndexOf(head.symbol_token_ids, symbolTokenIds[choice]);
                if (rowIndex < 0) throw new InvalidDataException("Legal symbol is absent from the original LM head.");
                var row = head.rows[rowIndex];
                double dot = 0;
                for (var i = 0; i < decisionHidden.Length; i++) dot += (double)decisionHidden[i] * row[i];
                if (double.IsNaN(dot) || double.IsInfinity(dot)) throw new InvalidDataException("LM-head logit is nonfinite.");
                logits[choice] = dot;
            }
            var maximum = logits[0];
            for (var i = 1; i < logits.Length; i++) maximum = Math.Max(maximum, logits[i]);
            var weights = new double[ChoiceCount];
            double sum = 0;
            for (var i = 0; i < logits.Length; i++) sum += weights[i] = Math.Exp(logits[i] - maximum);
            var probabilities = new float[ChoiceCount];
            for (var i = 0; i < probabilities.Length; i++) probabilities[i] = (float)(weights[i] / sum);
            PreparedCaseFile.ValidateProbabilities(probabilities, ChoiceCount);
            return probabilities;
        }

        public static string BuildCommand(string modelPath, int contextSize, string route = "embeddings")
        {
            if (string.IsNullOrWhiteSpace(modelPath) || modelPath.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0 ||
                !string.Equals(Path.GetExtension(modelPath), ".gguf", StringComparison.OrdinalIgnoreCase) ||
                contextSize < 1 || contextSize > 4096)
                throw new InvalidDataException("GGUF path or context size is invalid.");
            if (route != "embeddings" && route != "completion")
                throw new InvalidDataException("Route must be embeddings or completion.");
            // LlamaLib 2.0.5 parses LLAMA_EXAMPLE_SERVER options. --embd-normalize is
            // restricted to the standalone embedding/debug examples in its pinned llama.cpp.
            return "-m \"" + Path.GetFullPath(modelPath) + "\"" + (route == "embeddings" ? " --embedding --pooling none" : "") + " -c " + contextSize +
                " -b " + contextSize + " -ub " + contextSize + " -np 1 -ngl 0 -t 4 -tb 4 --log-verbosity 3";
        }

        public static string BuildEmbeddingRequest(int[] originalTokenIds)
        {
            if (originalTokenIds == null || originalTokenIds.Length == 0 || originalTokenIds.Length > 4096 ||
                Array.Exists(originalTokenIds, value => value < 0))
                throw new InvalidDataException("Embedding request requires the original unpadded token IDs.");
            // server-context.cpp at llama.cpp 872646b30c342c5fa98efec002f94fdb4f1f1ea5
            // accepts this request property. Pooling=none explicitly bypasses normalization
            // and returns each raw token row, irrespective of the requested normalization.
            return JsonConvert.SerializeObject(new { content = originalTokenIds, embd_normalize = -1 });
        }

        public static string BuildCompletionLogitBias(int[] legalTokenIds, int vocabularySize = CompletionVocabularySize)
        {
            if (legalTokenIds == null || legalTokenIds.Length != ChoiceCount || vocabularySize < ChoiceCount ||
                vocabularySize > CompletionVocabularySize || new HashSet<int>(legalTokenIds).Count != ChoiceCount ||
                Array.Exists(legalTokenIds, id => id < 0 || id >= vocabularySize))
                throw new InvalidDataException("Completion mask requires eight distinct legal IDs within the verified vocabulary.");
            var sorted = (int[])legalTokenIds.Clone();
            Array.Sort(sorted);
            var key = vocabularySize.ToString(CultureInfo.InvariantCulture) + ":" + string.Join(",", sorted);
            lock (CompletionBiasCache)
            {
                if (CompletionBiasCache.TryGetValue(key, out var cached)) return cached;
                var legal = new HashSet<int>(sorted);
                var builder = new StringBuilder(vocabularySize * 14);
                builder.Append('[');
                var first = true;
                for (var id = 0; id < vocabularySize; id++)
                {
                    if (legal.Contains(id)) continue;
                    if (!first) builder.Append(',');
                    first = false;
                    builder.Append('[').Append(id.ToString(CultureInfo.InvariantCulture)).Append(",false]");
                }
                builder.Append(']');
                var result = builder.ToString();
                CompletionBiasCache.Add(key, result);
                return result;
            }
        }

        public static string BuildCompletionRequest(InternDecisionUnityCase item)
        {
            if (item == null || item.input_ids == null || item.decision_position < 0 ||
                item.decision_position >= item.input_ids.Length - 1 ||
                Array.Exists(item.input_ids, id => id < 0 || id >= CompletionVocabularySize))
                throw new InvalidDataException("Completion requires the verified causal prefix through the decision position.");
            var prefix = new int[item.decision_position + 1];
            Array.Copy(item.input_ids, prefix, prefix.Length);
            var request = JsonConvert.SerializeObject(new
            {
                prompt = prefix, n_predict = 1, cache_prompt = false, backend_sampling = false,
                n_probs = ChoiceCount, post_sampling_probs = true, temperature = 1.0,
                dynatemp_range = 0.0, mirostat = 0, samplers = new[] { "top_k", "temperature" },
                top_k = ChoiceCount, seed = 0, stream = false,
                response_fields = new[] { "completion_probabilities", "timings", "tokens_evaluated", "tokens_predicted", "stop_type", "truncated" }
            });
            // The full-vocabulary bias is applied before samplers, making all eight and only
            // the eight original legal logits finite. top_k=8 then preserves their T=1 softmax.
            return request.Substring(0, request.Length - 1) + ",\"logit_bias\":" + BuildCompletionLogitBias(item.symbol_token_ids) + "}";
        }

        public static async Task<InternDecisionUnityReport> RunAsync(string manifestPath, string reportPath, int caseLimit = 0,
            CancellationToken cancellationToken = default, string route = "embeddings")
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(reportPath))
                throw new ArgumentException("Manifest and report paths are required.");
            var report = new InternDecisionUnityReport { manifest_path = Path.GetFullPath(manifestPath), stage = "validate", route = route };
            report.Save(reportPath);
            LLMService service = null;
            var log = new NativeLog();
            LlamaLib.CharArrayCallback loggingCallback = log.Capture;
            var acquired = false;
            var nativeInitialized = false;
            try
            {
                if (caseLimit < 0 || (route != "embeddings" && route != "completion"))
                    throw new InvalidDataException("Case limit or route is invalid.");
                var timer = Stopwatch.StartNew();
                var manifest = ReadJson<InternDecisionUnityManifest>(report.manifest_path);
                ValidateManifest(manifest);
                if (route == "completion")
                {
                    if (manifest.model_id != CompletionModelId || manifest.revision != CompletionModelRevision)
                        throw new InvalidDataException("Completion vocabulary masking is verified only for the pinned Intern-Decision-0.8B checkpoint.");
                    foreach (var item in manifest.cases)
                    {
                        if (Array.Exists(item.input_ids, id => id >= CompletionVocabularySize))
                            throw new InvalidDataException("Input ID exceeds the pinned model vocabulary.");
                        BuildCompletionLogitBias(item.symbol_token_ids);
                    }
                    report.completion_vocabulary_size = CompletionVocabularySize;
                    report.completion_logit_bias_entries = CompletionVocabularySize - ChoiceCount;
                    report.backend = "LLMUnity 3.0.3 / LlamaLib 2.0.5 CPU, single-position original LM-head legal-token conditional probabilities";
                    report.timing_scope = "Cached-mask request construction through native causal-prefix forward, original vocabulary LM head, explicit eight-token logit masking/T=1 probability calculation and HTTP parsing. No free-form answer is used; own argmax chooses the strategy. Native model/head/sampler costs are inseparable; head_ms=0 means no separate managed-head stage. Startup, file hashing and one separately recorded warm-up are excluded.";
                }
                report.decision_protocol_verified = manifest.decision_protocol != null;
                report.model_id = manifest.model_id;
                report.revision = manifest.revision;
                report.manifest_sha256 = HashFile(report.manifest_path);
                report.gguf_sha256 = manifest.gguf_sha256;
                report.head_sha256 = manifest.head_sha256;
                report.reference_result_sha256 = manifest.reference_result_sha256;
                report.context_size = manifest.context_size;
                report.hidden_size = manifest.hidden_size;
                var directory = Path.GetDirectoryName(report.manifest_path);
                var modelPath = ResolveArtifact(directory, manifest.gguf_file, ".gguf");
                var headPath = ResolveArtifact(directory, manifest.head_file, ".json");
                var referencePath = ResolveArtifact(directory, manifest.reference_result_file, ".json");
                GgufInputs.VerifyHash(modelPath, manifest.gguf_sha256);
                GgufInputs.VerifyHash(headPath, manifest.head_sha256);
                GgufInputs.VerifyHash(referencePath, manifest.reference_result_sha256);
                ValidateReference(ReadJson<JObject>(referencePath), manifest);
                report.reference_result_verified = true;
                var head = ReadJson<InternDecisionLmHead>(headPath);
                ValidateHead(head);
                foreach (var item in manifest.cases)
                    if (!new HashSet<int>(item.symbol_token_ids).SetEquals(head.symbol_token_ids))
                        throw new InvalidDataException("Case legal symbols disagree with the original LM head.");
                report.command = BuildCommand(modelPath, manifest.context_size, route);
                report.validation_ms = timer.Elapsed.TotalMilliseconds;
                cancellationToken.ThrowIfCancellationRequested();
                await Residency.WaitAsync(cancellationToken);
                acquired = true;
                if (LLMUnity.LLMUnitySetup.libraryProgress < 1)
                    throw new InvalidOperationException("LLMUnity native bundle is not ready.");
                report.stage = "native_startup";
                report.Save(reportPath);
                timer.Restart();
                service = await Task.Run(() =>
                {
                    var initializer = new LlamaLib(false);
                    nativeInitialized = true;
                    try
                    {
                        LlamaLib.Debug(5);
                        LlamaLib.LoggingCallback(loggingCallback);
                        return LLMService.FromCommand(report.command);
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
                if (!await service.StartAsync()) throw new InvalidOperationException("Intern-Decision native service could not start.");
                if (service.EmbeddingSize() != manifest.hidden_size)
                    throw new InvalidDataException("Native hidden width differs from the original LM head.");
                report.model_load_ms = timer.Elapsed.TotalMilliseconds;
                report.native_architecture = service.llamaLib.architecture;
                report.native_log = log.Lines;
                using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
                {
                    BaseAddress = new Uri("http://127.0.0.1:" + port), Timeout = Timeout.InfiniteTimeSpan
                };
                report.stage = "warmup";
                report.Save(reportPath);
                var warmup = route == "completion"
                    ? await InferCompletionAsync(client, manifest.cases[0], cancellationToken)
                    : await InferAsync(client, manifest.cases[0], head, cancellationToken);
                report.warmup_ms = warmup.inference_ms;
                report.warmup_processed_input_tokens = warmup.processed_input_tokens;
                var count = caseLimit == 0 ? manifest.cases.Length : Math.Min(caseLimit, manifest.cases.Length);
                report.cases = new InternDecisionUnityCaseResult[count];
                for (var i = 0; i < count; i++)
                {
                    report.stage = "case_" + i;
                    report.native_log = log.Lines;
                    report.Save(reportPath);
                    var result = route == "completion"
                        ? await InferCompletionAsync(client, manifest.cases[i], cancellationToken)
                        : await InferAsync(client, manifest.cases[i], head, cancellationToken);
                    report.cases[i] = result;
                    report.executed_cases++;
                    report.processed_input_tokens += result.processed_input_tokens;
                    if (result.selected_reference_matches) report.selected_reference_matches++;
                    report.max_reference_error = Math.Max(report.max_reference_error, result.max_reference_error);
                    report.mean_inference_ms += result.inference_ms / count;
                    report.mean_backbone_ms += result.backbone_ms / count;
                    report.mean_head_ms += result.head_ms / count;
                    report.Save(reportPath);
                    Debug.Log("Intern-Decision Unity case complete: " + result.case_id + ", ms=" + result.inference_ms.ToString("F1"));
                }
                report.execution_completed = true;
                report.success = true;
                report.diagnostic_parity_within_tolerance = report.executed_cases > 0 &&
                    report.selected_reference_matches == report.executed_cases &&
                    report.max_reference_error <= report.diagnostic_absolute_tolerance;
                report.stage = "complete";
            }
            catch (Exception exception)
            {
                report.error = exception.ToString();
                Debug.LogError("Intern-Decision Unity failed at " + report.stage + ": " + exception.Message);
            }
            finally
            {
                report.native_log = log.Lines;
                try { report.Save(reportPath); }
                catch (Exception checkpointError)
                {
                    report.success = false;
                    report.error = (report.error ?? "") + "\nReport checkpoint: " + checkpointError;
                }
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
                catch (Exception cleanup)
                {
                    report.success = false;
                    report.error = (report.error ?? "") + "\nNative cleanup: " + cleanup;
                }
                finally
                {
                    try { if (nativeInitialized) ResetLogging(); }
                    finally
                    {
                        GC.KeepAlive(loggingCallback);
                        if (acquired) Residency.Release();
                        report.native_log = log.Lines;
                        report.Save(reportPath);
                    }
                }
            }
            return report;
        }

        private static async Task<InternDecisionUnityCaseResult> InferAsync(HttpClient client, InternDecisionUnityCase item,
            InternDecisionLmHead head, CancellationToken cancellationToken)
        {
            using var content = new StringContent(BuildEmbeddingRequest(item.input_ids), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/embeddings") { Content = content };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(300));
            var timer = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                throw new InvalidDataException("Embedding response exceeds the byte limit.");
            using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[32768];
            int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > MaximumResponseBytes) throw new InvalidDataException("Embedding response exceeds the byte limit.");
                output.Write(buffer, 0, read);
            }
            var hidden = ParseDecisionEmbedding(Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length),
                item.input_ids.Length, head.hidden_size, item.decision_position);
            var backboneMs = timer.Elapsed.TotalMilliseconds;
            var probabilities = Predict(hidden, head, item.symbol_token_ids, out var logits);
            var elapsed = timer.Elapsed.TotalMilliseconds;
            cancellationToken.ThrowIfCancellationRequested();
            var chosen = ArgMax(probabilities);
            var referenceChosen = ArgMax(item.reference_probabilities);
            var result = new InternDecisionUnityCaseResult
            {
                case_id = item.case_id, input_token_count = item.input_ids.Length, decision_position = item.decision_position,
                processed_input_tokens = item.input_ids.Length, route = "embeddings",
                probability_source = "Managed original legal-token LM-head dot products and T=1 softmax; logits are raw legal logits.",
                raw_logits_available = true,
                choice_ids = item.choice_ids, symbol_token_ids = item.symbol_token_ids, logits = logits,
                probabilities = probabilities, reference_probabilities = item.reference_probabilities,
                chosen_id = item.choice_ids[chosen], reference_chosen_id = item.choice_ids[referenceChosen],
                selected_reference_matches = chosen == referenceChosen,
                inference_ms = elapsed, backbone_ms = backboneMs, head_ms = elapsed - backboneMs
            };
            for (var i = 0; i < probabilities.Length; i++)
                result.max_reference_error = Math.Max(result.max_reference_error, Math.Abs(probabilities[i] - item.reference_probabilities[i]));
            return result;
        }

        private static async Task<InternDecisionUnityCaseResult> InferCompletionAsync(HttpClient client, InternDecisionUnityCase item,
            CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();
            using var content = new StringContent(BuildCompletionRequest(item), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/completion") { Content = content };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(300));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            const int maximumCompletionBytes = 1024 * 1024;
            if (response.Content.Headers.ContentLength > maximumCompletionBytes)
                throw new InvalidDataException("Completion response exceeds the narrow probability-report limit.");
            using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > maximumCompletionBytes)
                    throw new InvalidDataException("Completion response exceeds the narrow probability-report limit.");
                output.Write(buffer, 0, read);
            }
            var parsed = ParseCompletionProbabilities(Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length),
                item.symbol_token_ids, item.decision_position + 1);
            var elapsed = timer.Elapsed.TotalMilliseconds;
            cancellationToken.ThrowIfCancellationRequested();
            var chosen = ArgMax(parsed.probabilities);
            var referenceChosen = ArgMax(item.reference_probabilities);
            var result = new InternDecisionUnityCaseResult
            {
                case_id = item.case_id, input_token_count = item.input_ids.Length,
                processed_input_tokens = parsed.tokens_evaluated, decision_position = item.decision_position,
                route = "completion", choice_ids = item.choice_ids, symbol_token_ids = item.symbol_token_ids,
                probability_source = "Native original LM head, illegal vocabulary logits masked before top_k=8 and T=1 softmax; direct legal conditional probabilities. Raw logits are unavailable.",
                raw_logits_available = false,
                logits = null, probabilities = parsed.probabilities, reference_probabilities = item.reference_probabilities,
                chosen_id = item.choice_ids[chosen], reference_chosen_id = item.choice_ids[referenceChosen],
                selected_reference_matches = chosen == referenceChosen,
                inference_ms = elapsed, backbone_ms = elapsed, head_ms = 0,
                has_native_timings = true, native_prompt_ms = parsed.prompt_ms,
                native_predicted_ms = parsed.predicted_ms, native_tokens_predicted = parsed.tokens_predicted
            };
            for (var i = 0; i < result.probabilities.Length; i++)
                result.max_reference_error = Math.Max(result.max_reference_error,
                    Math.Abs(result.probabilities[i] - item.reference_probabilities[i]));
            return result;
        }

        public static InternDecisionCompletionOutput ParseCompletionProbabilities(string json, int[] legalTokenIds, int expectedPromptTokens)
        {
            if (json == null || json.Length > 1024 * 1024 || expectedPromptTokens < 1 || expectedPromptTokens > 4096 ||
                legalTokenIds == null || legalTokenIds.Length != ChoiceCount ||
                new HashSet<int>(legalTokenIds).Count != ChoiceCount ||
                Array.Exists(legalTokenIds, id => id < 0 || id >= CompletionVocabularySize))
                throw new InvalidDataException("Completion probability response contract is invalid.");
            try
            {
                using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 16 };
                var response = JObject.Load(reader);
                if (reader.Read() || response.Value<int?>("tokens_evaluated") != expectedPromptTokens ||
                    response.Value<int?>("tokens_predicted") != 1 || response.Value<bool?>("truncated") != false ||
                    !(response["completion_probabilities"] is JArray positions) || positions.Count != 1 ||
                    !(positions[0] is JObject position) || !(position["top_probs"] is JArray probabilities) ||
                    probabilities.Count != ChoiceCount || !(response["timings"] is JObject timings))
                    throw new InvalidDataException("Completion must return one untruncated position and exactly eight legal token probabilities for the complete causal prefix.");
                var values = new float[ChoiceCount];
                var seen = new HashSet<int>();
                foreach (var entry in probabilities)
                {
                    if (!(entry is JObject token) || token["id"]?.Type != JTokenType.Integer ||
                        (token["prob"]?.Type != JTokenType.Float && token["prob"]?.Type != JTokenType.Integer))
                        throw new InvalidDataException("Completion token IDs and probabilities must be numeric.");
                    var id = token.Value<int>("id");
                    var index = Array.IndexOf(legalTokenIds, id);
                    var probability = token.Value<float>("prob");
                    if (index < 0 || !seen.Add(id) || !GgufInputs.IsFinite(probability) || probability < 0 || probability > 1)
                        throw new InvalidDataException("Completion returned an illegal/repeated symbol or invalid probability.");
                    values[index] = probability;
                }
                PreparedCaseFile.ValidateProbabilities(values, ChoiceCount);
                var promptMs = timings.Value<double?>("prompt_ms");
                var predictedMs = timings.Value<double?>("predicted_ms");
                if (!promptMs.HasValue || !predictedMs.HasValue || double.IsNaN(promptMs.Value) || double.IsInfinity(promptMs.Value) ||
                    double.IsNaN(predictedMs.Value) || double.IsInfinity(predictedMs.Value) || promptMs.Value < 0 || predictedMs.Value < 0)
                    throw new InvalidDataException("Native completion timings are missing or invalid.");
                return new InternDecisionCompletionOutput
                {
                    probabilities = values, tokens_evaluated = expectedPromptTokens, tokens_predicted = 1,
                    prompt_ms = promptMs.Value, predicted_ms = predictedMs.Value
                };
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Completion probability JSON is invalid.", exception);
            }
        }

        public static float[] ParseDecisionEmbedding(string json, int tokenCount, int hiddenSize, int decisionPosition)
        {
            if (json == null || json.Length > MaximumResponseBytes || tokenCount < 1 || tokenCount > 4096 ||
                hiddenSize != 1024 || decisionPosition < 0 || decisionPosition >= tokenCount)
                throw new InvalidDataException("Embedding dimensions or response size is invalid.");
            try
            {
                using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 8 };
                var response = JArray.Load(reader);
                if (reader.Read() || response.Count != 1 || !(response[0] is JObject result) ||
                    !(result["embedding"] is JArray rows) || rows.Count != tokenCount)
                    throw new InvalidDataException("Native embedding output must include every original input token in order.");
                var hidden = new float[hiddenSize];
                for (var token = 0; token < rows.Count; token++)
                {
                    if (!(rows[token] is JArray row) || row.Count != hiddenSize)
                        throw new InvalidDataException("Native embedding row has an unexpected width.");
                    for (var i = 0; i < row.Count; i++)
                    {
                        if (row[i].Type != JTokenType.Float && row[i].Type != JTokenType.Integer)
                            throw new InvalidDataException("Native embedding value must be numeric.");
                        var value = row[i].Value<float>();
                        if (!GgufInputs.IsFinite(value)) throw new InvalidDataException("Native embedding value must be finite.");
                        if (token == decisionPosition) hidden[i] = value;
                    }
                }
                return hidden;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Native embedding JSON is invalid.", exception);
            }
        }

        private static T ReadJson<T>(string path)
        {
            using var input = File.OpenText(path);
            using var reader = new JsonTextReader(input) { MaxDepth = 16 };
            var result = JsonSerializer.CreateDefault().Deserialize<T>(reader);
            if (reader.Read()) throw new InvalidDataException("Unexpected trailing JSON content.");
            return result;
        }

        private static void ValidateReference(JObject reference, InternDecisionUnityManifest manifest)
        {
            if (reference == null || reference.Value<string>("status") != "complete" ||
                reference.Value<string>("backend") != "intern" || reference.Value<string>("model_id") != manifest.model_id ||
                reference.Value<string>("revision") != manifest.revision ||
                (!string.IsNullOrEmpty(manifest.inputs_sha256) && reference.Value<string>("inputs_sha256") != manifest.inputs_sha256) ||
                !(reference["results"] is JArray results) || results.Count != manifest.cases.Length)
                throw new InvalidDataException("Referenced results are not a complete native HF run of the same pinned model and input suite.");
            for (var index = 0; index < manifest.cases.Length; index++)
            {
                var item = manifest.cases[index];
                if (!(results[index] is JObject row) || row.Value<string>("case_id") != item.case_id ||
                    row.Value<int?>("input_tokens") != item.input_ids.Length ||
                    (!string.IsNullOrEmpty(item.request_sha256) && row.Value<string>("request_sha256") != item.request_sha256) ||
                    !(row["candidate_ids"] is JArray choices) || choices.Count != ChoiceCount ||
                    !(row["probabilities"] is JObject probabilities) || probabilities.Count != ChoiceCount)
                    throw new InvalidDataException("Reference case identity, token count or candidate contract changed.");
                for (var choice = 0; choice < ChoiceCount; choice++)
                {
                    var probability = probabilities[item.choice_ids[choice]];
                    if (choices[choice].Type != JTokenType.String || choices[choice].Value<string>() != item.choice_ids[choice] ||
                        probability == null || (probability.Type != JTokenType.Float && probability.Type != JTokenType.Integer) ||
                        !GgufInputs.IsFinite(probability.Value<float>()) ||
                        Math.Abs(probability.Value<float>() - item.reference_probabilities[choice]) > 1e-7f)
                        throw new InvalidDataException("Inline reference probabilities or candidate order differ from the hashed native HF result.");
                }
                if (row.Value<string>("selected_id") != item.choice_ids[ArgMax(item.reference_probabilities)])
                    throw new InvalidDataException("Reference selected ID disagrees with its inline probability distribution.");
            }
        }

        private static int ArgMax(float[] values)
        {
            var index = 0;
            for (var i = 1; i < values.Length; i++) if (values[i] > values[index]) index = i;
            return index;
        }

        private static string ResolveArtifact(string directory, string file, string extension)
        {
            if (string.IsNullOrWhiteSpace(file) || file.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0 ||
                !string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid local artifact path.");
            return Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(directory, file));
        }

        private static bool IsHash(string value) => value != null && value.Length == 64 &&
            Array.TrueForAll(value.ToCharArray(), Uri.IsHexDigit);

        private static string HashFile(string path)
        {
            using var input = File.OpenRead(path);
            using var hash = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        private static void ResetLogging()
        {
            try { LlamaLib.LoggingStop(); }
            finally { LlamaLib.Debug(0); }
        }

        private sealed class NativeLog
        {
            private readonly object _sync = new object();
            private readonly List<string> _lines = new List<string>();
            public string[] Lines { get { lock (_sync) return _lines.ToArray(); } }
            public void Capture(string message)
            {
                if (string.IsNullOrWhiteSpace(message)) return;
                lock (_sync)
                    foreach (var line in message.Split('\n'))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (_lines.Count == 128) _lines.RemoveAt(0);
                        _lines.Add(line.Length > 1024 ? line.Substring(0, 1024) : line);
                    }
            }
        }
    }
}
