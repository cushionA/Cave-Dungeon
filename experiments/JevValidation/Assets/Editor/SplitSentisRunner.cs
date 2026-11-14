using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace JevValidation
{
    [Serializable]
    public sealed class SplitSentisManifest
    {
        public int schema_version;
        public string model_id;
        public string model_revision;
        public string source_onnx_sha256;
        public int token_count;
        public int hidden_size;
        public string case_file;
        public string case_sha256;
        public SplitSentisChunk[] chunks;
    }

    [Serializable]
    public sealed class SplitSentisChunk
    {
        public string name;
        public string file;
        public string sha256;
        public string[] inputs;
        public string[] outputs;
    }

    [Serializable]
    public sealed class SplitSentisMemorySample
    {
        public string stage;
        public long working_set_bytes;
        public long private_bytes;
        public long unity_allocated_bytes;
        public long unity_reserved_bytes;
    }

    [Serializable]
    public sealed class SplitSentisChunkMeasurement
    {
        public string name;
        public string source_file;
        public string imported_asset;
        public string sha256;
        public long onnx_bytes;
        public bool destination_existed;
        public double copy_ms;
        public double import_ms;
        public double cached_asset_lookup_ms;
        public double model_load_ms;
        public double worker_create_ms;
        public bool import_succeeded;
        public bool import_skipped;
        public bool worker_created;
        public SplitSentisMemorySample before_import;
        public SplitSentisMemorySample after_import;
        public SplitSentisMemorySample after_worker;
    }

    [Serializable]
    public sealed class SplitSentisReport
    {
        public int schema_version = 1;
        public string created_utc = DateTime.UtcNow.ToString("O");
        public string unity_version = Application.unityVersion;
        public string operating_system = SystemInfo.operatingSystem;
        public string processor = SystemInfo.processorType;
        public string graphics_device = SystemInfo.graphicsDeviceName;
        public string manifest_path;
        public string manifest_sha256;
        public string case_file_sha256;
        public string source_onnx_sha256;
        public string mode;
        public string backend;
        public bool reimport_requested = true;
        public bool success;
        public bool execution_completed;
        public bool parity_passed;
        public float parity_absolute_tolerance = 0.005f;
        public string stage;
        public string error;
        public string memory_scope = "Point-in-time process and Unity allocator samples; not peak memory. Use the external process sampler for peak RSS.";
        public string inference_scope = "All chunk schedules through final probability readback, excluding input construction, model load and one separately recorded warm-up.";
        public double total_import_ms;
        public double total_model_load_ms;
        public double total_worker_create_ms;
        public double warmup_ms;
        public double mean_inference_ms;
        public int reference_case_count;
        public int selected_reference_matches;
        public float max_reference_error;
        public SplitSentisMemorySample initial_memory;
        public SplitSentisMemorySample before_inference;
        public SplitSentisMemorySample final_memory;
        public SplitSentisChunkMeasurement[] chunks;
        public ValidationReport predictions;

        public void Save(string path)
        {
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, JsonUtility.ToJson(this, true));
        }
    }

    public static class SplitSentisRunner
    {
        private const string ImportDirectory = "Assets/Models/SplitTinyJev";
        private static readonly SemaphoreSlim Residency = new SemaphoreSlim(1, 1);
        private static readonly string[] PreparedInputs =
            { "input_ids", "attention_mask", "option_positions", "decision_position" };

        public static void ValidateManifest(SplitSentisManifest manifest)
        {
            if (manifest == null || manifest.schema_version != 1 ||
                manifest.model_id != "AnkitAI/TinyJev-0.6B" ||
                string.IsNullOrWhiteSpace(manifest.model_revision) ||
                manifest.token_count != PreparedCaseFile.TokenCount || manifest.hidden_size != 1024 ||
                !IsHash(manifest.source_onnx_sha256) || !IsHash(manifest.case_sha256))
                throw new InvalidDataException("Split manifest identity, SHA or fixed 256-token/1024-hidden contract is invalid.");
            ValidateFileName(manifest.case_file, ".json");
            if (manifest.chunks == null || manifest.chunks.Length < 2)
                throw new InvalidDataException("At least two split models are required.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var available = new HashSet<string>(PreparedInputs, StringComparer.Ordinal);
            for (var index = 0; index < manifest.chunks.Length; index++)
            {
                var chunk = manifest.chunks[index];
                if (chunk == null || string.IsNullOrWhiteSpace(chunk.name) || !names.Add(chunk.name) ||
                    !IsHash(chunk.sha256))
                    throw new InvalidDataException("Split model names must be unique and hashes must be SHA-256.");
                ValidateFileName(chunk.file, ".onnx");
                if (!files.Add(chunk.file) || chunk.inputs == null || chunk.inputs.Length == 0 ||
                    new HashSet<string>(chunk.inputs, StringComparer.Ordinal).Count != chunk.inputs.Length ||
                    Array.Exists(chunk.inputs, name => string.IsNullOrWhiteSpace(name) || !available.Contains(name)) ||
                    chunk.outputs == null || chunk.outputs.Length != 1 || string.IsNullOrWhiteSpace(chunk.outputs[0]) ||
                    available.Contains(chunk.outputs[0]))
                    throw new InvalidDataException("Split model filenames or tensor dependency order are invalid.");
                if (index > 0 && !Array.Exists(chunk.inputs, name => name == manifest.chunks[index - 1].outputs[0]))
                    throw new InvalidDataException("Every split must consume the immediately preceding hidden state.");
                available.Add(chunk.outputs[0]);
            }
            if (manifest.chunks[manifest.chunks.Length - 1].outputs[0] != "probabilities")
                throw new InvalidDataException("The final split must output probabilities.");
        }

        public static Task<SplitSentisReport> FromEnvironmentAsync()
        {
            var manifest = Environment.GetEnvironmentVariable("JEV_SPLIT_MANIFEST");
            var report = Environment.GetEnvironmentVariable("JEV_SPLIT_REPORT");
            var mode = Environment.GetEnvironmentVariable("JEV_SPLIT_MODE") ?? "resident";
            var backendText = Environment.GetEnvironmentVariable("JEV_SPLIT_BACKEND") ?? "CPU";
            var limitText = Environment.GetEnvironmentVariable("JEV_SPLIT_CASE_LIMIT");
            var reimportText = Environment.GetEnvironmentVariable("JEV_SPLIT_REIMPORT");
            if (!Enum.TryParse(backendText, true, out BackendType backend))
                throw new InvalidDataException("JEV_SPLIT_BACKEND must be CPU or GPUCompute.");
            var limit = string.IsNullOrEmpty(limitText) ? 0 : int.Parse(limitText);
            var reimport = true;
            if (!string.IsNullOrEmpty(reimportText) && !bool.TryParse(reimportText, out reimport))
                throw new InvalidDataException("JEV_SPLIT_REIMPORT must be true or false.");
            return RunAsync(manifest, report, mode, backend, limit, reimport: reimport);
        }

        public static SplitSentisReport ProbeMonolithic(string sourceOnnx, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(sourceOnnx) || string.IsNullOrWhiteSpace(reportPath))
                throw new ArgumentException("Source ONNX and report paths are required.");
            var source = Path.GetFullPath(sourceOnnx);
            var report = new SplitSentisReport
            {
                manifest_path = source, mode = "monolithic_probe", backend = BackendType.CPU.ToString(),
                initial_memory = SampleMemory("initial"), stage = "validate"
            };
            Worker worker = null;
            try
            {
                var assetPath = "Assets/Models/MonolithicTinyJev/tinyjev.onnx";
                var hash = HashFile(source);
                var measurement = new SplitSentisChunkMeasurement
                {
                    name = "monolithic", source_file = source, imported_asset = assetPath,
                    sha256 = hash, onnx_bytes = new FileInfo(source).Length,
                    destination_existed = File.Exists(assetPath), before_import = SampleMemory("before_import")
                };
                report.source_onnx_sha256 = hash;
                report.chunks = new[] { measurement };
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                report.stage = "copy";
                report.Save(reportPath);
                var timer = Stopwatch.StartNew();
                if (!string.Equals(source, Path.GetFullPath(assetPath), StringComparison.Ordinal))
                    File.Copy(source, assetPath, true);
                measurement.copy_ms = timer.Elapsed.TotalMilliseconds;
                report.stage = "import";
                report.Save(reportPath);
                timer.Restart();
                ModelAsset asset;
                try
                {
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(assetPath);
                }
                finally
                {
                    measurement.import_ms = timer.Elapsed.TotalMilliseconds;
                    report.total_import_ms = measurement.import_ms;
                    measurement.after_import = SampleMemory("after_import");
                }
                if (asset == null) throw new InvalidDataException("No monolithic ModelAsset was produced.");
                measurement.import_succeeded = true;
                report.stage = "model_load";
                report.Save(reportPath);
                timer.Restart();
                var model = ModelLoader.Load(asset);
                measurement.model_load_ms = timer.Elapsed.TotalMilliseconds;
                report.total_model_load_ms = measurement.model_load_ms;
                if (model.inputs.Count != 4 || model.outputs.Count != 1 || model.outputs[0].name != "probabilities")
                    throw new InvalidDataException("The monolithic graph does not match the original four-input contract.");
                report.stage = "worker_create";
                report.Save(reportPath);
                timer.Restart();
                worker = new Worker(model, BackendType.CPU);
                measurement.worker_create_ms = timer.Elapsed.TotalMilliseconds;
                report.total_worker_create_ms = measurement.worker_create_ms;
                measurement.worker_created = true;
                measurement.after_worker = SampleMemory("after_worker");
                report.stage = "complete";
                report.execution_completed = true;
                report.success = true;
            }
            catch (Exception exception)
            {
                report.error = exception.ToString();
                Debug.LogError("JEV monolithic probe failed at " + report.stage + ": " + exception.Message);
            }
            finally
            {
                worker?.Dispose();
                report.final_memory = SampleMemory("after_worker_dispose");
                report.Save(reportPath);
            }
            return report;
        }

        // Launch without -quit; this method exits only after the async readback and report complete.
        public static void Batch()
        {
            Task<SplitSentisReport> task;
            try { task = FromEnvironmentAsync(); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                return;
            }
            void Poll()
            {
                if (!task.IsCompleted) return;
                EditorApplication.update -= Poll;
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogException(task.Exception?.GetBaseException() ?? new OperationCanceledException());
                    EditorApplication.Exit(1);
                    return;
                }
                EditorApplication.Exit(task.Result.success ? 0 : 1);
            }
            EditorApplication.update += Poll;
        }

        /// <summary>
        /// Imports splits and validates resident inference. With reimport=false, resident mode only
        /// opens existing hash-matching imported assets; import mode always measures a forced import.
        /// </summary>
        public static async Task<SplitSentisReport> RunAsync(string manifestPath, string reportPath,
            string mode = "resident", BackendType backend = BackendType.CPU, int caseLimit = 0,
            CancellationToken cancellationToken = default, float absoluteTolerance = 0.005f, bool reimport = true)
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(reportPath))
                throw new ArgumentException("Manifest and report paths are required.");
            var report = new SplitSentisReport
            {
                manifest_path = Path.GetFullPath(manifestPath), mode = mode, backend = backend.ToString(),
                initial_memory = SampleMemory("initial"), stage = "validate", parity_absolute_tolerance = absoluteTolerance,
                reimport_requested = reimport
            };
            var workers = new List<Worker>();
            await Residency.WaitAsync(cancellationToken);
            try
            {
                if (mode != "import" && mode != "resident")
                    throw new InvalidDataException("Mode must be import or resident.");
                if (caseLimit < 0 || float.IsNaN(absoluteTolerance) || float.IsInfinity(absoluteTolerance) ||
                    absoluteTolerance <= 0 || (backend != BackendType.CPU && backend != BackendType.GPUCompute))
                    throw new InvalidDataException("Case limit or backend is invalid.");
                if (backend == BackendType.GPUCompute && !SystemInfo.supportsComputeShaders)
                    throw new InvalidOperationException("GPUCompute is unavailable on this graphics device.");
                var manifest = JsonUtility.FromJson<SplitSentisManifest>(File.ReadAllText(report.manifest_path));
                ValidateManifest(manifest);
                report.manifest_sha256 = HashFile(report.manifest_path);
                report.source_onnx_sha256 = manifest.source_onnx_sha256;
                var directory = Path.GetDirectoryName(report.manifest_path);
                var casePath = Path.Combine(directory, manifest.case_file);
                var suite = PreparedCaseFile.Read(casePath);
                report.case_file_sha256 = HashFile(casePath);
                if (!string.Equals(report.case_file_sha256, manifest.case_sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Cases JSON SHA-256 does not match the split manifest.");
                if (suite.model_id != manifest.model_id || suite.model_revision != manifest.model_revision ||
                    !string.Equals(suite.model_sha256, manifest.source_onnx_sha256, StringComparison.OrdinalIgnoreCase) ||
                    suite.model_format == "gguf" || suite.token_count != manifest.token_count)
                    throw new InvalidDataException("Cases must identify the same original ONNX and fixed input contract.");
                foreach (var chunk in manifest.chunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = Path.Combine(directory, chunk.file);
                    if (!string.Equals(HashFile(source), chunk.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SHA-256 mismatch: " + chunk.file);
                }
                report.chunks = new SplitSentisChunkMeasurement[manifest.chunks.Length];
                Directory.CreateDirectory(ImportDirectory);
                var models = new List<Model>();
                for (var index = 0; index < manifest.chunks.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var chunk = manifest.chunks[index];
                    var source = Path.Combine(directory, chunk.file);
                    var assetPath = ImportDirectory + "/" + chunk.file;
                    var measurement = new SplitSentisChunkMeasurement
                    {
                        name = chunk.name, source_file = source, imported_asset = assetPath,
                        sha256 = chunk.sha256, onnx_bytes = new FileInfo(source).Length,
                        destination_existed = File.Exists(assetPath), before_import = SampleMemory("before_import_" + index)
                    };
                    report.chunks[index] = measurement;
                    var timer = Stopwatch.StartNew();
                    ModelAsset asset;
                    if (mode == "import" || reimport)
                    {
                        report.stage = "copy_" + index;
                        report.Save(reportPath);
                        timer.Restart();
                        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(assetPath), StringComparison.Ordinal))
                            File.Copy(source, assetPath, true);
                        measurement.copy_ms = timer.Elapsed.TotalMilliseconds;
                        report.stage = "import_" + index;
                        report.Save(reportPath);
                        timer.Restart();
                        try
                        {
                            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                            asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(assetPath);
                        }
                        finally
                        {
                            measurement.import_ms = timer.Elapsed.TotalMilliseconds;
                            report.total_import_ms += measurement.import_ms;
                            measurement.after_import = SampleMemory("after_import_" + index);
                        }
                        if (asset == null) throw new InvalidDataException("No ModelAsset was produced: " + assetPath);
                        measurement.import_succeeded = true;
                    }
                    else
                    {
                        report.stage = "cached_asset_" + index;
                        measurement.import_skipped = true;
                        report.Save(reportPath);
                        if (!measurement.destination_existed ||
                            !string.Equals(HashFile(assetPath), chunk.sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Existing imported ONNX is missing or SHA-256 differs: " + assetPath);
                        timer.Restart();
                        asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(assetPath);
                        measurement.cached_asset_lookup_ms = timer.Elapsed.TotalMilliseconds;
                        measurement.after_import = SampleMemory("after_cached_asset_lookup_" + index);
                        if (asset == null) throw new InvalidDataException("No existing imported ModelAsset was found: " + assetPath);
                    }
                    if (mode == "resident")
                    {
                        report.stage = "model_load_" + index;
                        report.Save(reportPath);
                        timer.Restart();
                        var model = ModelLoader.Load(asset);
                        measurement.model_load_ms = timer.Elapsed.TotalMilliseconds;
                        report.total_model_load_ms += measurement.model_load_ms;
                        ValidateModelInputs(model, chunk, manifest);
                        models.Add(model);
                        report.stage = "worker_create_" + index;
                        report.Save(reportPath);
                        timer.Restart();
                        workers.Add(new Worker(model, backend));
                        measurement.worker_create_ms = timer.Elapsed.TotalMilliseconds;
                        report.total_worker_create_ms += measurement.worker_create_ms;
                        measurement.worker_created = true;
                        measurement.after_worker = SampleMemory("after_worker_" + index);
                    }
                    Debug.Log("JEV split ready: " + chunk.name + ", import_ms=" + measurement.import_ms.ToString("F1"));
                    report.Save(reportPath);
                }
                if (mode == "resident")
                {
                    report.before_inference = SampleMemory("before_inference");
                    report.stage = "warmup";
                    report.Save(reportPath);
                    var warmup = await Predict(workers, models, manifest, suite.cases[0], cancellationToken);
                    report.warmup_ms = warmup.milliseconds;
                    var count = caseLimit == 0 ? suite.cases.Length : Math.Min(caseLimit, suite.cases.Length);
                    var values = new float[count][];
                    var milliseconds = new double[count];
                    for (var index = 0; index < count; index++)
                    {
                        report.stage = "case_" + index;
                        report.Save(reportPath);
                        var result = await Predict(workers, models, manifest, suite.cases[index], cancellationToken);
                        values[index] = result.values;
                        milliseconds[index] = result.milliseconds;
                        report.mean_inference_ms += result.milliseconds / count;
                        Debug.Log("JEV split case complete: " + suite.cases[index].case_id + ", ms=" + result.milliseconds.ToString("F1"));
                    }
                    if (count != suite.cases.Length) Array.Resize(ref suite.cases, count);
                    report.predictions = ReportBuilder.Build(suite, "Sentis split " + backend, report.manifest_path, values, milliseconds);
                    foreach (var result in report.predictions.cases)
                    {
                        if (!result.has_reference) continue;
                        report.reference_case_count++;
                        report.max_reference_error = Math.Max(report.max_reference_error, result.max_reference_error);
                        var selected = 0;
                        for (var i = 1; i < result.reference_probabilities.Length; i++)
                            if (result.reference_probabilities[i] > result.reference_probabilities[selected]) selected = i;
                        if (result.chosen_id == result.choice_ids[selected]) report.selected_reference_matches++;
                    }
                    report.parity_passed = report.reference_case_count == count && count > 0 &&
                        report.selected_reference_matches == count && report.max_reference_error <= absoluteTolerance;
                    if (!report.parity_passed)
                        report.error = "Reference parity failed: every executed case requires reference values, matching selected ID, and maximum absolute probability error <= " + absoluteTolerance;
                }
                report.stage = "complete";
                report.execution_completed = true;
                report.success = mode == "import" || report.parity_passed;
            }
            catch (Exception exception)
            {
                report.error = exception.ToString();
                Debug.LogError("JEV split failed at " + report.stage + ": " + exception.Message);
            }
            finally
            {
                foreach (var worker in workers) worker.Dispose();
                report.final_memory = SampleMemory("after_worker_dispose");
                report.Save(reportPath);
                Residency.Release();
            }
            return report;
        }

        private static async Task<(float[] values, double milliseconds)> Predict(List<Worker> workers,
            List<Model> models, SplitSentisManifest manifest, PreparedCase item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var ids = new Tensor<int>(new TensorShape(1, manifest.token_count), item.input_ids);
            using var mask = new Tensor<int>(new TensorShape(1, manifest.token_count), item.attention_mask);
            using var options = new Tensor<int>(new TensorShape(item.choice_ids.Length), item.option_positions);
            using var decision = new Tensor<int>(new TensorShape(1), item.decision_position);
            var tensors = new Dictionary<string, Tensor>(StringComparer.Ordinal)
            {
                ["input_ids"] = ids, ["attention_mask"] = mask,
                ["option_positions"] = options, ["decision_position"] = decision
            };
            var timer = Stopwatch.StartNew();
            for (var index = 0; index < workers.Count; index++)
            {
                foreach (var input in models[index].inputs)
                {
                    if (!tensors.TryGetValue(input.name, out var tensor))
                        throw new InvalidDataException("No tensor available for " + input.name);
                    workers[index].SetInput(input.name, tensor);
                }
                workers[index].Schedule();
                var outputName = manifest.chunks[index].outputs[0];
                tensors[outputName] = workers[index].PeekOutput(outputName);
            }
            if (!(tensors["probabilities"] is Tensor<float> output))
                throw new InvalidDataException("Final output must be float probabilities.");
            // All workers and their borrowed outputs remain alive until the final readback completes.
            using var readable = await output.ReadbackAndCloneAsync();
            timer.Stop();
            cancellationToken.ThrowIfCancellationRequested();
            if (readable.shape.rank != 1 || readable.shape[0] != item.choice_ids.Length)
                throw new InvalidDataException("Split probabilities have an unexpected shape.");
            var values = readable.DownloadToArray();
            PreparedCaseFile.ValidateProbabilities(values, item.choice_ids.Length);
            return (values, timer.Elapsed.TotalMilliseconds);
        }

        private static void ValidateModelInputs(Model model, SplitSentisChunk chunk, SplitSentisManifest manifest)
        {
            if (model.inputs.Count != chunk.inputs.Length || model.outputs.Count != 1 ||
                model.outputs[0].name != chunk.outputs[0])
                throw new InvalidDataException("Imported graph inputs/outputs disagree with " + chunk.name);
            foreach (var input in model.inputs)
            {
                if (Array.IndexOf(chunk.inputs, input.name) < 0 || !input.shape.IsStatic())
                    throw new InvalidDataException("Unexpected or dynamic split input: " + input.name);
                var isPrepared = Array.IndexOf(PreparedInputs, input.name) >= 0;
                var shape = isPrepared
                    ? input.name == "option_positions" ? new TensorShape(PreparedCaseFile.ChoiceCount)
                        : input.name == "decision_position" ? new TensorShape(1)
                        : new TensorShape(1, manifest.token_count)
                    : new TensorShape(1, manifest.token_count, manifest.hidden_size);
                if (input.dataType != (isPrepared ? DataType.Int : DataType.Float) || !input.shape.ToTensorShape().Equals(shape))
                    throw new InvalidDataException("Split tensor dtype/shape mismatch: " + input.name);
            }
        }

        private static SplitSentisMemorySample SampleMemory(string stage)
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            return new SplitSentisMemorySample
            {
                stage = stage, working_set_bytes = process.WorkingSet64, private_bytes = process.PrivateMemorySize64,
                unity_allocated_bytes = Profiler.GetTotalAllocatedMemoryLong(), unity_reserved_bytes = Profiler.GetTotalReservedMemoryLong()
            };
        }

        private static void ValidateFileName(string name, string extension)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." ||
                name.IndexOfAny(new[] { '/', '\\', '\0', ':', '\r', '\n' }) >= 0 ||
                !string.Equals(Path.GetExtension(name), extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Split artifact references must be local basenames with extension " + extension);
        }

        private static bool IsHash(string value) => value != null && value.Length == 64 &&
            Array.TrueForAll(value.ToCharArray(), Uri.IsHexDigit);

        private static string HashFile(string path)
        {
            using var source = File.OpenRead(path);
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
        }
    }
}
