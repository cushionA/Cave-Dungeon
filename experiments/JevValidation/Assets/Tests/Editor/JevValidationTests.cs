using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace JevValidation.Tests
{
    public sealed class JevValidationTests
    {
        private const string CasesPath = "Assets/PreparedCases/smoke.json";
        private const string ModelPath = "Assets/Models/smoke.onnx";

        [Test]
        public void PreparedCaseFile_ValidFixture_PreservesInputsAndOptionalReference()
        {
            var suite = PreparedCaseFile.Read(CasesPath);
            Assert.That(suite.model_id, Is.EqualTo("smoke"));
            Assert.That(suite.token_count, Is.EqualTo(256));
            Assert.That(suite.cases, Has.Length.EqualTo(2));
            Assert.That(suite.cases[0].choice_ids, Is.EqualTo(new[] { "wait", "approach", "attack", "retreat" }));
            Assert.That(suite.cases[0].option_positions, Is.EqualTo(new[] { 4, 8, 12, 16 }));
            Assert.That(suite.cases[0].decision_position, Is.EqualTo(new[] { 20 }));
            suite.cases[1].reference_probabilities = null;
            Assert.DoesNotThrow(() => PreparedCaseFile.Validate(suite));
        }

        [Test]
        public void PreparedCaseFile_InvalidBoundaryDataOrModelHash_RejectsInput()
        {
            Action<PreparedSuite>[] corruptions =
            {
                suite => suite.token_count = 255,
                suite => suite.cases[0].choice_ids = new[] { "a", "b", "c" },
                suite => suite.cases[0].input_ids = new int[255],
                suite => suite.cases[0].attention_mask[0] = 2,
                suite => suite.cases[0].option_positions[0] = 256,
                suite => suite.cases[0].reference_probabilities = new[] { float.NaN, 0f, 0f, 1f },
                suite => suite.cases[0].reference_probabilities = new[] { .1f, .1f, .1f, .1f }
            };
            foreach (var corrupt in corruptions)
            {
                var suite = Fixture();
                corrupt(suite);
                Assert.Throws<InvalidDataException>(() => PreparedCaseFile.Validate(suite));
            }

            var wrongModel = Fixture();
            wrongModel.model_sha256 = new string('0', 64);
            Assert.Throws<InvalidDataException>(() => SentisCaseRunner.ValidateModelHash(
                AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath), wrongModel));
        }

        [Test]
        public void ReportBuilder_ReorderedChoicesAndChangedState_AlignsSensitivityAndReferenceError()
        {
            var suite = Fixture();
            suite.cases[0].reference_probabilities = new[] { .2f, .2f, .3f, .3f };
            suite.cases[1].choice_ids = new[] { "retreat", "attack", "approach", "wait" };
            suite.cases[1].reference_probabilities = null;
            var report = ReportBuilder.Build(suite, "CPU", ModelPath,
                new[] { new[] { .1f, .2f, .3f, .4f }, new[] { .1f, .2f, .3f, .4f } }, new[] { 1.5, 2.5 });

            Assert.That(report.cases[0].max_reference_error, Is.EqualTo(.1f).Within(1e-6));
            Assert.That(report.cases[0].mean_reference_error, Is.EqualTo(.05f).Within(1e-6));
            Assert.That(report.cases[1].has_reference, Is.False);
            Assert.That(report.state_comparisons, Has.Length.EqualTo(1));
            Assert.That(report.state_comparisons[0].probability_total_variation, Is.EqualTo(.4f).Within(1e-6));
            Assert.That(report.state_comparisons[0].choice_changed, Is.True);
            Assert.That(report.state_comparisons[0].has_reference, Is.False);
        }

        [UnityTest]
        public IEnumerator SentisCaseRunner_SmokeCpu_MatchesReferenceAndSavesReport()
        {
            var operation = SentisCaseRunner.RunAsync(AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath),
                PreparedCaseFile.Read(CasesPath), BackendType.CPU);
            while (!operation.IsCompleted)
                yield return null;
            var report = operation.GetAwaiter().GetResult();
            Assert.That(report.model_id, Is.EqualTo("smoke"));
            Assert.That(report.model_kind, Is.EqualTo("smoke"));
            Assert.That(report.cases.All(item => item.has_reference && item.max_reference_error < 1e-4f), Is.True);
            Assert.That(report.cases.All(item => item.inference_ms >= 0), Is.True);
            Assert.That(report.state_comparisons[0].choice_changed, Is.True);

            var path = Path.Combine(Path.GetTempPath(), "jev-smoke-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                report.Save(path);
                var saved = JsonUtility.FromJson<ValidationReport>(File.ReadAllText(path));
                Assert.That(saved.model_sha256, Is.EqualTo(report.model_sha256));
                Assert.That(saved.cases[0].probabilities, Is.EqualTo(report.cases[0].probabilities));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [UnityTest]
        public IEnumerator SentisCaseRunner_SmokeGpu_MatchesReference()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("GPUCompute is unavailable on this graphics device.");
            var operation = SentisCaseRunner.RunAsync(AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath),
                PreparedCaseFile.Read(CasesPath), BackendType.GPUCompute);
            while (!operation.IsCompleted)
                yield return null;
            var report = operation.GetAwaiter().GetResult();
            Assert.That(report.cases, Has.Length.EqualTo(2));
            Assert.That(report.cases.All(item => item.max_reference_error < 1e-4f), Is.True);
        }

        private static PreparedSuite Fixture() =>
            JsonUtility.FromJson<PreparedSuite>(File.ReadAllText(CasesPath));
    }
}
