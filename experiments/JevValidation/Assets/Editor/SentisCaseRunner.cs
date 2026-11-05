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

namespace JevValidation
{
    public static class SentisCaseRunner
    {
        public static void ValidateModelHash(ModelAsset asset, PreparedSuite suite)
        {
            if (suite.model_format == "gguf" || suite.token_count != PreparedCaseFile.TokenCount ||
                Array.Exists(suite.cases, item => item.choice_ids.Length != PreparedCaseFile.ChoiceCount))
                throw new InvalidDataException("SentisはONNXの4候補/256token固定です。");
            var path = AssetDatabase.GetAssetPath(asset);
            if (asset == null || !string.Equals(Path.GetExtension(path), ".onnx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("AssetsにimportしたONNXモデルを選択してください。");
            using var source = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            var actual = BitConverter.ToString(sha256.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
            if (!string.Equals(actual, suite.model_sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ONNXのSHA256がケースJSONのmodel_sha256と一致しません。");
        }

        public static async Task<ValidationReport> RunAsync(ModelAsset asset, PreparedSuite suite,
            BackendType backend, CancellationToken cancellationToken = default)
        {
            PreparedCaseFile.Validate(suite);
            ValidateModelHash(asset, suite);
            if (backend != BackendType.CPU && backend != BackendType.GPUCompute)
                throw new ArgumentOutOfRangeException(nameof(backend), "CPUまたはGPUComputeを指定してください。");
            if (backend == BackendType.GPUCompute && !SystemInfo.supportsComputeShaders)
                throw new InvalidOperationException("このgraphics deviceはGPUComputeに対応していません。");
            cancellationToken.ThrowIfCancellationRequested();
            var model = ModelLoader.Load(asset);
            ValidateInputs(model);
            using var worker = new Worker(model, backend);
            await Predict(worker, suite.cases[0], cancellationToken);
            var probabilities = new float[suite.cases.Length][];
            var milliseconds = new double[suite.cases.Length];
            for (var index = 0; index < suite.cases.Length; index++)
            {
                var prediction = await Predict(worker, suite.cases[index], cancellationToken);
                probabilities[index] = prediction.values;
                milliseconds[index] = prediction.milliseconds;
            }
            return ReportBuilder.Build(suite, backend.ToString(), AssetDatabase.GetAssetPath(asset), probabilities, milliseconds);
        }

        private static void ValidateInputs(Model model)
        {
            var expected = new Dictionary<string, TensorShape>
            {
                ["input_ids"] = new TensorShape(1, PreparedCaseFile.TokenCount),
                ["attention_mask"] = new TensorShape(1, PreparedCaseFile.TokenCount),
                ["option_positions"] = new TensorShape(PreparedCaseFile.ChoiceCount),
                ["decision_position"] = new TensorShape(1)
            };
            if (model.inputs.Count != expected.Count || model.outputs.Count != 1 || model.outputs[0].name != "probabilities")
                throw new InvalidDataException("モデルの4入力とprobabilities出力が契約に一致しません。");
            foreach (var input in model.inputs)
                if (!expected.TryGetValue(input.name, out var shape) || input.dataType != DataType.Int ||
                    !input.shape.IsStatic() || !input.shape.ToTensorShape().Equals(shape))
                    throw new InvalidDataException("モデル入力の名前・int32型・固定shapeが契約に一致しません: " + input.name);
        }

        private static async Task<(float[] values, double milliseconds)> Predict(Worker worker,
            PreparedCase item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var ids = new Tensor<int>(new TensorShape(1, PreparedCaseFile.TokenCount), item.input_ids);
            using var mask = new Tensor<int>(new TensorShape(1, PreparedCaseFile.TokenCount), item.attention_mask);
            using var options = new Tensor<int>(new TensorShape(PreparedCaseFile.ChoiceCount), item.option_positions);
            using var decision = new Tensor<int>(new TensorShape(1), item.decision_position);
            worker.SetInput("input_ids", ids);
            worker.SetInput("attention_mask", mask);
            worker.SetInput("option_positions", options);
            worker.SetInput("decision_position", decision);
            var timer = Stopwatch.StartNew();
            worker.Schedule();
            if (!(worker.PeekOutput("probabilities") is Tensor<float> output))
                throw new InvalidDataException("probabilities出力はfloat32である必要があります。");
            using var readable = await output.ReadbackAndCloneAsync();
            timer.Stop();
            cancellationToken.ThrowIfCancellationRequested();
            if (readable.shape.rank != 1 || readable.shape[0] != PreparedCaseFile.ChoiceCount)
                throw new InvalidDataException("probabilities出力のshapeは[4]である必要があります。");
            var values = readable.DownloadToArray();
            PreparedCaseFile.ValidateProbabilities(values);
            return (values, timer.Elapsed.TotalMilliseconds);
        }
    }
}
