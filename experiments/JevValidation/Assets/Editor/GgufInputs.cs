using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JevValidation
{
    public static class GgufInputs
    {
        public const int MaximumResponseCharacters = 16 * 1024 * 1024;

        public static string ResolveHeadPath(string casePath, PreparedSuite suite)
        {
            if (suite.model_format != "gguf" || suite.model_id != "AnkitAI/TinyJev-0.6B" ||
                (suite.backbone_quantization != "F16" && suite.backbone_quantization != "Q8_0" && suite.backbone_quantization != "Q4_K_M") ||
                !IsSha256(suite.head_sha256) || string.IsNullOrWhiteSpace(suite.head_file) ||
                suite.head_file == "." || suite.head_file == ".." || Path.IsPathRooted(suite.head_file) ||
                suite.head_file.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new InvalidDataException("GGUFにはTinyJevのhead basename・SHA256・F16/Q8_0/Q4_K_Mの指定が必要です。");
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(casePath)), suite.head_file);
        }

        public static int[] ActiveTokenIds(PreparedCase item)
        {
            var count = 0;
            while (count < item.attention_mask.Length && item.attention_mask[count] == 1) count++;
            if (count == 0) throw new InvalidDataException("有効tokenがありません。");
            for (var index = count; index < item.attention_mask.Length; index++)
                if (item.attention_mask[index] != 0) throw new InvalidDataException("GGUF入力は右paddingの連続maskだけに対応します。");
            var tokens = new int[count];
            Array.Copy(item.input_ids, tokens, count);
            if (Array.Exists(tokens, value => value < 0 || value >= 151936))
                throw new InvalidDataException("TinyJevのtoken IDは0から151935が必要です。");
            return tokens;
        }

        public static float[][] ParseEmbeddings(string json, int activeCount)
        {
            if (json.Length > MaximumResponseCharacters || activeCount < 1 || activeCount > PreparedCaseFile.MaximumTokenCount)
                throw new InvalidDataException("Embedding応答の大きさが上限を超えています。");
            try
            {
                using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 8 };
                var response = JArray.Load(reader);
                if (reader.Read() || response.Count != 1 || !(response[0] is JObject result) ||
                    !(result["embedding"] is JArray rows) || rows.Count != activeCount)
                    throw new InvalidDataException("Embedding応答は全有効tokenの行が必要です。");
                var hidden = new float[activeCount][];
                for (var token = 0; token < activeCount; token++)
                {
                    if (!(rows[token] is JArray row) || row.Count != JevPointerHead.HiddenSize)
                        throw new InvalidDataException("Embedding行は1024要素が必要です。");
                    hidden[token] = new float[JevPointerHead.HiddenSize];
                    for (var index = 0; index < hidden[token].Length; index++)
                    {
                        if (row[index].Type != JTokenType.Float && row[index].Type != JTokenType.Integer)
                            throw new InvalidDataException("Embedding要素は数値が必要です。");
                        var value = row[index].Value<float>();
                        if (!IsFinite(value)) throw new InvalidDataException("Embedding要素は有限値が必要です。");
                        hidden[token][index] = value;
                    }
                }
                return hidden;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Embedding JSONを読み取れません。", exception);
            }
        }

        public static void VerifyHash(string path, string expected)
        {
            if (!IsSha256(expected)) throw new InvalidDataException("64桁のSHA256が必要です。");
            using var algorithm = SHA256.Create();
            using var stream = File.OpenRead(path);
            var actual = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(Path.GetFileName(path) + ": SHA256が一致しません。");
        }

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsSha256(string value) => value != null && value.Length == 64 && Array.TrueForAll(value.ToCharArray(), Uri.IsHexDigit);
    }
}
