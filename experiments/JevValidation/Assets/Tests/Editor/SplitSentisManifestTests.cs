using System;
using System.IO;
using NUnit.Framework;

namespace JevValidation.Tests
{
    public sealed class SplitSentisManifestTests
    {
        [Test]
        public void SplitManifest_CompleteSequentialGraph_IsAccepted()
        {
            Assert.DoesNotThrow(() => SplitSentisRunner.ValidateManifest(Fixture()));
        }

        [Test]
        public void SplitManifest_ArtifactEscapesSourceDirectory_IsRejectedBeforeImport()
        {
            foreach (var path in new[] { "../cases.json", "sub/cases.json", "sub\\cases.json", "/cases.json", "C:cases.json" })
            {
                var manifest = Fixture();
                manifest.case_file = path;
                Assert.Throws<InvalidDataException>(() => SplitSentisRunner.ValidateManifest(manifest), path);
            }
            var chunkEscape = Fixture();
            chunkEscape.chunks[0].file = "../embedding.onnx";
            Assert.Throws<InvalidDataException>(() => SplitSentisRunner.ValidateManifest(chunkEscape));
        }

        [Test]
        public void SplitManifest_StaleMissingOrDisconnectedHiddenState_IsRejected()
        {
            Action<SplitSentisManifest>[] corruptions =
            {
                manifest => manifest.chunks[1].inputs = new[] { "hidden_missing", "attention_mask" },
                manifest => manifest.chunks[1].inputs = new[] { "input_ids", "attention_mask" },
                manifest => manifest.chunks[1].outputs = new[] { "input_ids" },
                manifest => manifest.chunks[1].outputs = new[] { "not_probabilities" },
                manifest => manifest.chunks[1].inputs = new[] { "hidden_0", "hidden_0" }
            };
            foreach (var corrupt in corruptions)
            {
                var manifest = Fixture();
                corrupt(manifest);
                Assert.Throws<InvalidDataException>(() => SplitSentisRunner.ValidateManifest(manifest));
            }
        }

        [Test]
        public void SplitManifest_ReusedAssetsOrInvalidIdentity_IsRejected()
        {
            Action<SplitSentisManifest>[] corruptions =
            {
                manifest => manifest.chunks[1].file = manifest.chunks[0].file,
                manifest => manifest.chunks[1].name = manifest.chunks[0].name,
                manifest => manifest.chunks[0].sha256 = new string('z', 64),
                manifest => manifest.source_onnx_sha256 = "short",
                manifest => manifest.token_count = 512,
                manifest => manifest.hidden_size = 1023,
                manifest => manifest.model_id = "smoke"
            };
            foreach (var corrupt in corruptions)
            {
                var manifest = Fixture();
                corrupt(manifest);
                Assert.Throws<InvalidDataException>(() => SplitSentisRunner.ValidateManifest(manifest));
            }
        }

        private static SplitSentisManifest Fixture() => new SplitSentisManifest
        {
            schema_version = 1,
            model_id = "AnkitAI/TinyJev-0.6B",
            model_revision = "c559c2f7ea95069f92af8b999f512345bbb87d22",
            source_onnx_sha256 = new string('1', 64),
            token_count = 256,
            hidden_size = 1024,
            case_file = "cases.json",
            case_sha256 = new string('4', 64),
            chunks = new[]
            {
                new SplitSentisChunk
                {
                    name = "embedding", file = "embedding.onnx", sha256 = new string('2', 64),
                    inputs = new[] { "input_ids" }, outputs = new[] { "hidden_0" }
                },
                new SplitSentisChunk
                {
                    name = "body_and_head", file = "body.onnx", sha256 = new string('3', 64),
                    inputs = new[] { "hidden_0", "attention_mask", "option_positions", "decision_position" },
                    outputs = new[] { "probabilities" }
                }
            }
        };
    }
}
