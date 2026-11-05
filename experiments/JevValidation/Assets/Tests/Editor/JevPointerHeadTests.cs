using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace JevValidation.Editor.Tests
{
    public sealed class JevPointerHeadTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "jev-head-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
            Directory.Delete(_directory);
        }

        [Test]
        public void JevHead_ValidBinary_ReproducesKnownSoftmax()
        {
            var path = WriteHead();
            var head = JevPointerHead.Load(path, Hash(path));
            var hidden = Enumerable.Range(0, 5).Select(_ => new float[1024]).ToArray();
            for (var index = 0; index < 4; index++) hidden[index][1] = (float)Math.Log(index + 1) + .25f;
            hidden[4][0] = .5f;
            var result = head.Predict(hidden, new PreparedCase { option_positions = new[] { 0, 1, 2, 3 }, decision_position = new[] { 4 } });
            Assert.That(result, Is.EqualTo(new[] { .1f, .2f, .3f, .4f }).Within(1e-6));
        }

        [Test]
        public void JevHead_CorruptBinaryOrDigest_IsRejected()
        {
            var path = WriteHead();
            Assert.Throws<InvalidDataException>(() => JevPointerHead.Load(path, new string('0', 64)));
            using (var stream = File.OpenWrite(path)) stream.SetLength(20);
            Assert.Throws<InvalidDataException>(() => JevPointerHead.Load(path, Hash(path)));
            path = WriteHead(temperature: 0);
            Assert.Throws<InvalidDataException>(() => JevPointerHead.Load(path, Hash(path)));
            path = WriteHead(nonFinite: true);
            Assert.Throws<InvalidDataException>(() => JevPointerHead.Load(path, Hash(path)));
            path = WriteHead();
            using (var stream = File.OpenWrite(path))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = 8;
                writer.Write(int.MaxValue);
            }
            Assert.Throws<InvalidDataException>(() => JevPointerHead.Load(path, Hash(path)));
            path = WriteHead();
            using (var stream = File.OpenWrite(path)) stream.WriteByte(0);
            Assert.Throws<InvalidDataException>(() => JevPointerHead.Load(path, Hash(path)));
        }

        [Test]
        public void GgufContract_HeadMetadata_RequiresBoundedBasenameAndKnownQuantization()
        {
            var suite = new PreparedSuite { model_id = "AnkitAI/TinyJev-0.6B", model_format = "gguf", head_file = "tinyjev.head", head_sha256 = new string('a', 64), backbone_quantization = "F16" };
            var cases = Path.Combine(_directory, "cases.json");
            Assert.That(GgufInputs.ResolveHeadPath(cases, suite), Is.EqualTo(Path.Combine(_directory, "tinyjev.head")));
            suite.head_file = "../tinyjev.head";
            Assert.Throws<InvalidDataException>(() => GgufInputs.ResolveHeadPath(cases, suite));
            suite.head_file = "tinyjev.head";
            suite.backbone_quantization = "generic-qwen";
            Assert.Throws<InvalidDataException>(() => GgufInputs.ResolveHeadPath(cases, suite));
            suite.backbone_quantization = "Q4_K_M";
            suite.head_sha256 = "bad";
            Assert.Throws<InvalidDataException>(() => GgufInputs.ResolveHeadPath(cases, suite));
        }

        [Test]
        public void GgufEmbeddings_Response_RetainsAllTokensAndRejectsInvalidVectors()
        {
            var vector = "[" + string.Join(",", Enumerable.Repeat("0.125", 1024)) + "]";
            var reply = "[{\"embedding\":[" + vector + "," + vector + "]}]";
            var hidden = GgufInputs.ParseEmbeddings(reply, 2);
            Assert.That(hidden.Length, Is.EqualTo(2));
            Assert.That(hidden[1][1023], Is.EqualTo(.125f));
            Assert.Throws<InvalidDataException>(() => GgufInputs.ParseEmbeddings(reply, 3));
            Assert.Throws<InvalidDataException>(() => GgufInputs.ParseEmbeddings("[{\"embedding\":[[1],[2]]}]", 2));
            Assert.Throws<InvalidDataException>(() => GgufInputs.ParseEmbeddings(reply.Replace("0.125", "NaN"), 2));
        }

        [Test]
        public void GgufTokens_RightPadding_IsRemovedWithoutChangingPositions()
        {
            var item = new PreparedCase { input_ids = Enumerable.Range(0, 256).ToArray(), attention_mask = Enumerable.Range(0, 256).Select(index => index < 5 ? 1 : 0).ToArray() };
            Assert.That(GgufInputs.ActiveTokenIds(item), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            item.attention_mask[1] = 0;
            Assert.Throws<InvalidDataException>(() => GgufInputs.ActiveTokenIds(item));
            Array.Clear(item.attention_mask, 0, item.attention_mask.Length);
            Assert.Throws<InvalidDataException>(() => GgufInputs.ActiveTokenIds(item));
            item.attention_mask[0] = 1;
            item.input_ids[0] = 151936;
            Assert.Throws<InvalidDataException>(() => GgufInputs.ActiveTokenIds(item));
        }

        private string WriteHead(float temperature = 1, bool nonFinite = false)
        {
            var path = Path.Combine(_directory, "tinyjev.head");
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(Encoding.ASCII.GetBytes("JEVHEAD1"));
            writer.Write(1024);
            writer.Write(256);
            writer.Write(temperature);
            for (var index = 0; index < 256 * 1024; index++) writer.Write(index == 0 ? (nonFinite ? float.NaN : 16f) : 0);
            for (var index = 0; index < 256; index++) writer.Write(index == 0 ? 8f : 0);
            for (var index = 0; index < 256 * 1024; index++) writer.Write(index == 1 ? 1f : 0);
            for (var index = 0; index < 256; index++) writer.Write(index == 0 ? -.25f : 0);
            return path;
        }

        private static string Hash(string path)
        {
            using var algorithm = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
