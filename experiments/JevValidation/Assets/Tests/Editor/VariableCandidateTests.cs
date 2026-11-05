using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace JevValidation.Editor.Tests
{
    public sealed class VariableCandidateTests
    {
        [Test]
        public void PreparedCases_GgufSupportsFourThroughEightAndBothLengths_WhileLegacyStaysFixed()
        {
            foreach (int length in new[] { 256, 512 })
            {
                for (int count = 4; count <= 8; count++)
                {
                    PreparedSuite suite = Suite(count, length);
                    suite.cases[0].option_positions[count - 1] = length - 1;
                    Assert.DoesNotThrow(() => PreparedCaseFile.Validate(suite));
                }
            }
            PreparedSuite legacy = PreparedCaseFile.Read("Assets/PreparedCases/smoke.json");
            Assert.That(legacy.token_count, Is.EqualTo(256));
            Assert.That(legacy.cases[0].choice_ids.Length, Is.EqualTo(4));
            PreparedSuite wrongLegacyLength = Suite(4, 512);
            wrongLegacyLength.model_format = "onnx";
            Assert.Throws<InvalidDataException>(() => PreparedCaseFile.Validate(wrongLegacyLength));
            PreparedSuite wrongLegacyChoices = Suite(8, 256);
            wrongLegacyChoices.model_format = "onnx";
            Assert.Throws<InvalidDataException>(() => PreparedCaseFile.Validate(wrongLegacyChoices));
        }

        [Test]
        public void PreparedCases_GgufInvalidCountsShapesPositionsAndProbabilities_AreRejected()
        {
            Action<PreparedSuite>[] corruptions =
            {
                suite => suite.cases[0] = Case("bad", 3, 512),
                suite => suite.cases[0] = Case("bad", 9, 512),
                suite => suite.token_count = 257,
                suite => suite.cases[0].input_ids = new int[511],
                suite => suite.cases[0].attention_mask = new int[511],
                suite => suite.cases[0].option_positions = new int[7],
                suite => suite.cases[0].option_positions[7] = 512,
                suite => suite.cases[0].reference_probabilities = new[] { .25f, .25f, .25f, .25f },
                suite => suite.cases[0].reference_probabilities[7] = float.NaN,
                suite => suite.cases[0].reference_probabilities[7] = 0f
            };
            foreach (Action<PreparedSuite> corrupt in corruptions)
            {
                PreparedSuite suite = Suite(8, 512);
                corrupt(suite);
                Assert.Throws<InvalidDataException>(() => PreparedCaseFile.Validate(suite));
            }
            PreparedSuite mixed = Suite(8, 512);
            mixed.cases[1] = Case("four", 4, 512);
            Assert.DoesNotThrow(() => PreparedCaseFile.Validate(mixed));
        }

        [Test]
        public void JevHead_EighthLogitCanBeLargest_AndFourChoiceProbabilitiesArePreserved()
        {
            string path = Path.Combine(Path.GetTempPath(), "jev-var-head-" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteHead(path);
                using SHA256 hash = SHA256.Create();
                using FileStream stream = File.OpenRead(path);
                string sha = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                JevPointerHead head = JevPointerHead.Load(path, sha);
                float[][] hidden = Enumerable.Range(0, 9).Select(_ => new float[1024]).ToArray();
                hidden[8][0] = 1f;
                hidden[7][1] = 1000f;
                float[] eight = head.Predict(hidden, new PreparedCase
                {
                    option_positions = Enumerable.Range(0, 8).ToArray(),
                    decision_position = new[] { 8 }
                });
                Assert.That(eight, Has.Length.EqualTo(8));
                Assert.That(eight[7], Is.EqualTo(1f));
                Assert.That(eight.Take(7), Is.All.EqualTo(0f));

                for (int index = 0; index < 4; index++) hidden[index][1] = (float)Math.Log(index + 1);
                float[] four = head.Predict(hidden, new PreparedCase
                {
                    option_positions = new[] { 0, 1, 2, 3 },
                    decision_position = new[] { 8 }
                });
                Assert.That(four, Is.EqualTo(new[] { .1f, .2f, .3f, .4f }).Within(1e-6));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Embeddings_Full512TokenResponse_IsAcceptedAndCountOverflowIsRejected()
        {
            string row = "[" + string.Join(",", Enumerable.Repeat("0.125", 1024)) + "]";
            string response = "[{\"embedding\":[" + string.Join(",", Enumerable.Repeat(row, 512)) + "]}]";
            float[][] hidden = GgufInputs.ParseEmbeddings(response, 512);
            Assert.That(hidden, Has.Length.EqualTo(512));
            Assert.That(hidden[511][1023], Is.EqualTo(.125f));
            Assert.Throws<InvalidDataException>(() => GgufInputs.ParseEmbeddings(response, 513));
            Assert.Throws<InvalidDataException>(() => GgufInputs.ParseEmbeddings(response, 256));
        }

        [Test]
        public void Reports_MixedCandidateCounts_UsePerCaseMeansAndAlignReorderedStrategyIds()
        {
            PreparedSuite suite = Suite(8, 512);
            PreparedCase reordered = Case("eight-reordered", 8, 512);
            reordered.choice_ids = new[] { "choice2", "choice1", "choice0", "choice3", "choice4", "choice5", "choice6", "choice7" };
            suite.cases = new[] { Case("four", 4, 512), Case("eight", 8, 512), reordered };
            float[][] values =
            {
                new[] { 1f, 0f, 0f, 0f },
                new[] { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f }
            };
            ValidationReport report = ReportBuilder.Build(suite, "test", "model.gguf", values, new[] { 1d, 2d, 3d });
            Assert.That(report.cases[0].mean_reference_error, Is.EqualTo(.375f));
            Assert.That(report.cases[1].mean_reference_error, Is.EqualTo(.21875f));
            Assert.That(report.cases[2].mean_reference_error, Is.EqualTo(.21875f));
            Assert.That(report.state_comparisons, Has.Length.EqualTo(1));
            Assert.That(report.state_comparisons[0].probability_total_variation, Is.Zero);
            Assert.That(report.state_comparisons[0].reference_total_variation, Is.Zero);
            Assert.That(report.state_comparisons[0].choice_changed, Is.False);
        }

        private static PreparedSuite Suite(int count, int length)
        {
            return new PreparedSuite
            {
                schema_version = 1,
                model_id = "AnkitAI/TinyJev-0.6B",
                model_revision = "variable-test",
                model_sha256 = new string('a', 64),
                model_format = "gguf",
                head_file = "tinyjev.head",
                head_sha256 = new string('b', 64),
                backbone_quantization = "F16",
                token_count = length,
                cases = new[] { Case("first", count, length), Case("second", count, length) }
            };
        }

        private static PreparedCase Case(string id, int count, int length)
        {
            return new PreparedCase
            {
                case_id = id,
                state_id = id,
                choice_ids = Enumerable.Range(0, count).Select(index => "choice" + index).ToArray(),
                input_ids = new int[length],
                attention_mask = Enumerable.Repeat(1, length).ToArray(),
                option_positions = Enumerable.Range(0, count).ToArray(),
                decision_position = new[] { 20 },
                reference_probabilities = Enumerable.Repeat(1f / count, count).ToArray()
            };
        }

        private static void WriteHead(string path)
        {
            using BinaryWriter writer = new BinaryWriter(File.Create(path));
            writer.Write(Encoding.ASCII.GetBytes("JEVHEAD1"));
            writer.Write(1024);
            writer.Write(256);
            writer.Write(1f);
            for (int index = 0; index < 256 * 1024; index++) writer.Write(index == 0 ? 16f : 0f);
            for (int index = 0; index < 256; index++) writer.Write(0f);
            for (int index = 0; index < 256 * 1024; index++) writer.Write(index == 1 ? 1f : 0f);
            for (int index = 0; index < 256; index++) writer.Write(0f);
        }
    }
}
