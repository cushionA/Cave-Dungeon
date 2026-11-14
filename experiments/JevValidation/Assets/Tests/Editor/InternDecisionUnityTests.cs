using System;
using System.IO;
using System.Linq;
using System.Globalization;
using NUnit.Framework;

namespace JevValidation.Tests
{
    public sealed class InternDecisionUnityTests
    {
        [Test]
        public void OriginalLmHead_UsesLegalSymbolRowsInCaseOrder_WithStableSoftmax()
        {
            var head = Head();
            for (var i = 0; i < head.rows.Length; i++) head.rows[i][0] = 1000 + i;
            var hidden = new float[1024];
            hidden[0] = 1;
            var forward = InternDecisionUnityRunner.Predict(hidden, head, head.symbol_token_ids, out var logits);
            var reversed = InternDecisionUnityRunner.Predict(hidden, head, head.symbol_token_ids.Reverse().ToArray(), out var reversedLogits);
            Assert.That(logits, Is.EqualTo(Enumerable.Range(1000, 8).Select(i => (double)i).ToArray()));
            Assert.That(reversedLogits, Is.EqualTo(logits.Reverse().ToArray()));
            Assert.That(reversed, Is.EqualTo(forward.Reverse().ToArray()));
            Assert.That(forward.Sum(), Is.EqualTo(1).Within(1e-6));
            Assert.That(forward[7], Is.GreaterThan(forward[0]));
            Assert.That(forward.All(value => !float.IsNaN(value) && !float.IsInfinity(value)), Is.True);
        }

        [Test]
        public void OriginalLmHead_MissingSymbolOrNonfiniteWeights_IsRejected()
        {
            var head = Head();
            var unknown = (int[])head.symbol_token_ids.Clone();
            unknown[0] = 9999;
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.Predict(new float[1024], head, unknown, out _));
            head.rows[4][0] = float.NaN;
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.ValidateHead(head));
        }

        [Test]
        public void Manifest_RejectsOutOfContextOrPostMarkerDecisionPosition()
        {
            Assert.DoesNotThrow(() => InternDecisionUnityRunner.ValidateManifest(Manifest()));
            Action<InternDecisionUnityManifest>[] corruptions =
            {
                manifest => manifest.cases[0].decision_position = manifest.cases[0].input_ids.Length - 1,
                manifest => manifest.cases[0].decision_position = -1,
                manifest => manifest.cases[0].decision_position = 0,
                manifest => manifest.context_size = 2,
                manifest => manifest.cases[0].symbol_token_ids[1] = manifest.cases[0].symbol_token_ids[0],
                manifest => manifest.cases[0].reference_probabilities[0] = float.NaN,
                manifest => manifest.reference_result_sha256 = "invalid"
            };
            foreach (var corrupt in corruptions)
            {
                var manifest = Manifest();
                corrupt(manifest);
                Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.ValidateManifest(manifest));
            }
        }

        [Test]
        public void EmbeddingResponse_UsesExactDecisionRow_AndRejectsMissingTokenRows()
        {
            var first = "[" + string.Join(",", Enumerable.Repeat("2.5", 1024)) + "]";
            var second = "[" + string.Join(",", Enumerable.Repeat("-7", 1024)) + "]";
            var json = "[{\"embedding\":[" + first + "," + second + "]}]";
            var hidden = InternDecisionUnityRunner.ParseDecisionEmbedding(json, 2, 1024, 0);
            Assert.That(hidden, Has.Length.EqualTo(1024));
            Assert.That(hidden.All(value => value == 2.5f), Is.True);
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.ParseDecisionEmbedding(json, 3, 1024, 0));
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.ParseDecisionEmbedding(json, 2, 1024, 2));
        }

        [Test]
        public void NativeCommand_PreservesRawEmbeddingAndCpuContextContract()
        {
            var command = InternDecisionUnityRunner.BuildCommand("/tmp/intern-f16.gguf", 2048);
            Assert.That(command, Does.Contain("--embedding --pooling none"));
            Assert.That(command, Does.Not.Contain("--embd-normalize"));
            Assert.That(command, Does.Contain("-c 2048 -b 2048 -ub 2048"));
            Assert.That(command, Does.Contain("-ngl 0 -t 4 -tb 4"));
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.BuildCommand("model\" --extra.gguf", 2048));
        }

        [Test]
        public void EmbeddingRequest_PreservesAllOriginalIds_AndUsesSupportedRequestNormalizationProperty()
        {
            var ids = new[] { 11, 12, 13, 14 };
            var json = InternDecisionUnityRunner.BuildEmbeddingRequest(ids);
            Assert.That(json, Is.EqualTo("{\"content\":[11,12,13,14],\"embd_normalize\":-1}"));
            Assert.That(ids, Is.EqualTo(new[] { 11, 12, 13, 14 }));
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.BuildEmbeddingRequest(new[] { -1 }));
        }

        [Test]
        public void CompletionMask_DisablesEveryIllegalVocabularyId_AndCachesByLegalSet()
        {
            var legal = Enumerable.Range(32, 8).ToArray();
            var mask = InternDecisionUnityRunner.BuildCompletionLogitBias(legal);
            Assert.That(mask, Does.StartWith("[[0,false],[1,false]"));
            Assert.That(mask, Does.EndWith("[248319,false]]"));
            Assert.That(mask.Count(character => character == ']'), Is.EqualTo(248320 - 8 + 1));
            foreach (var id in legal) Assert.That(mask, Does.Not.Contain("[" + id + ",false]"));
            Assert.That(ReferenceEquals(mask, InternDecisionUnityRunner.BuildCompletionLogitBias(legal.Reverse().ToArray())), Is.True);
            Assert.That(InternDecisionUnityRunner.BuildCompletionLogitBias(Enumerable.Range(0, 8).ToArray(), 12),
                Is.EqualTo("[[8,false],[9,false],[10,false],[11,false]]"));
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.BuildCompletionLogitBias(Enumerable.Range(5, 8).ToArray(), 12));
        }

        [Test]
        public void CompletionRequest_UsesOnlyCausalPrefix_AndPreservesExactLegalConditionalSoftmax()
        {
            var item = Manifest().cases[0];
            item.symbol_token_ids = Enumerable.Range(32, 8).ToArray();
            var json = InternDecisionUnityRunner.BuildCompletionRequest(item);
            Assert.That(json, Does.StartWith("{\"prompt\":[11,12,13],\"n_predict\":1,"));
            Assert.That(item.input_ids, Is.EqualTo(new[] { 11, 12, 13, 14 }));
            Assert.That(json, Does.Contain("\"cache_prompt\":false"));
            Assert.That(json, Does.Contain("\"backend_sampling\":false"));
            Assert.That(json, Does.Contain("\"n_probs\":8,\"post_sampling_probs\":true"));
            Assert.That(json, Does.Contain("\"temperature\":1.0,\"dynatemp_range\":0.0,\"mirostat\":0"));
            Assert.That(json, Does.Contain("\"samplers\":[\"top_k\",\"temperature\"],\"top_k\":8"));
            Assert.That(json, Does.Contain("\"response_fields\":[\"completion_probabilities\",\"timings\",\"tokens_evaluated\",\"tokens_predicted\",\"stop_type\",\"truncated\"]"));
            Assert.That(json, Does.Contain("\"logit_bias\":[[0,false]"));
            Assert.That(json, Does.Not.Contain("\"grammar\""));
            var command = InternDecisionUnityRunner.BuildCommand("/tmp/intern-f16.gguf", 1024, "completion");
            Assert.That(command, Does.Not.Contain("--embedding"));
            Assert.That(command, Does.Not.Contain("--pooling"));
            Assert.That(command, Does.Not.Contain("--embd-normalize"));
        }

        [Test]
        public void CompletionProbabilities_AlignsExactLegalIds_AndRejectsIncompleteOrUnnormalizedResults()
        {
            var legal = Enumerable.Range(32, 8).ToArray();
            var entries = legal.Reverse().Select(id => (id, probability: (id - 31) / 36.0)).ToArray();
            var json = CompletionResponse(entries);
            var result = InternDecisionUnityRunner.ParseCompletionProbabilities(json, legal, 3);
            Assert.That(result.tokens_evaluated, Is.EqualTo(3));
            Assert.That(result.tokens_predicted, Is.EqualTo(1));
            Assert.That(result.probabilities, Is.EqualTo(Enumerable.Range(1, 8).Select(i => (float)(i / 36.0)).ToArray()).Within(1e-6));
            Assert.That(result.prompt_ms, Is.EqualTo(12.5));
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.ParseCompletionProbabilities(json, legal, 4));
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.ParseCompletionProbabilities(CompletionResponse(entries.Take(7).ToArray()), legal, 3));
            var duplicate = ((int id, double probability)[])entries.Clone();
            duplicate[1].id = duplicate[0].id;
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.ParseCompletionProbabilities(CompletionResponse(duplicate), legal, 3));
            var illegal = ((int id, double probability)[])entries.Clone();
            illegal[0].id = 31;
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.ParseCompletionProbabilities(CompletionResponse(illegal), legal, 3));
            var wrongMass = legal.Select(id => (id, probability: 0.01)).ToArray();
            Assert.Throws<InvalidDataException>(() => InternDecisionUnityRunner.ParseCompletionProbabilities(CompletionResponse(wrongMass), legal, 3));
        }

        private static string CompletionResponse((int id, double probability)[] entries) =>
            "{\"tokens_evaluated\":3,\"tokens_predicted\":1,\"truncated\":false,\"stop_type\":\"limit\"," +
            "\"timings\":{\"prompt_ms\":12.5,\"predicted_ms\":0.5},\"completion_probabilities\":[{\"top_probs\":[" +
            string.Join(",", entries.Select(entry => "{\"id\":" + entry.id.ToString(CultureInfo.InvariantCulture) +
                ",\"prob\":" + entry.probability.ToString("R", CultureInfo.InvariantCulture) + "}")) + "]}]}";

        private static InternDecisionLmHead Head() => new InternDecisionLmHead
        {
            hidden_size = 1024,
            rows = Enumerable.Range(0, 8).Select(_ => new float[1024]).ToArray(),
            symbol_token_ids = Enumerable.Range(100, 8).ToArray()
        };

        private static InternDecisionUnityManifest Manifest() => new InternDecisionUnityManifest
        {
            schema_version = 1, model_id = "Intern-Decision-test", revision = "pinned-revision",
            gguf_file = "model.gguf", gguf_sha256 = new string('a', 64),
            head_file = "head.json", head_sha256 = new string('b', 64),
            hidden_size = 1024, context_size = 1024,
            reference_result_file = "reference.json", reference_result_sha256 = new string('c', 64),
            decision_protocol = new InternDecisionUnityProtocol { temperature = 1, decision_token_id = 14 },
            cases = new[]
            {
                new InternDecisionUnityCase
                {
                    case_id = "example", input_ids = new[] { 11, 12, 13, 14 }, decision_position = 2,
                    choice_ids = Enumerable.Range(0, 8).Select(i => "choice_" + i).ToArray(),
                    symbol_token_ids = Enumerable.Range(100, 8).ToArray(),
                    reference_probabilities = Enumerable.Repeat(0.125f, 8).ToArray()
                }
            }
        };
    }
}
