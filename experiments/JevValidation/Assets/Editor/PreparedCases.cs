using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace JevValidation
{
    [Serializable]
    public sealed class PreparedSuite
    {
        public int schema_version;
        public string model_id;
        public string model_revision;
        public string model_sha256;
        public string model_format;
        public string head_file;
        public string head_sha256;
        public string backbone_quantization;
        public int token_count;
        public PreparedCase[] cases;
    }

    [Serializable]
    public sealed class PreparedCase
    {
        public string case_id;
        public string state_id;
        public string state;
        public string[] choice_ids;
        public int[] input_ids;
        public int[] attention_mask;
        public int[] option_positions;
        public int[] decision_position;
        public float[] reference_probabilities;
    }

    public static class PreparedCaseFile
    {
        public const int TokenCount = 256;
        public const int ChoiceCount = 4;
        public const int MaximumTokenCount = 512;
        public const int MaximumChoiceCount = 8;

        public static PreparedSuite Read(string path)
        {
            PreparedSuite suite;
            try
            {
                suite = JsonUtility.FromJson<PreparedSuite>(File.ReadAllText(path));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("ケースJSONを読み取れません。", exception);
            }
            Validate(suite);
            return suite;
        }

        public static void Validate(PreparedSuite suite)
        {
            if (suite == null || suite.schema_version != 1)
                throw new InvalidDataException("schema_version=1が必要です。");
            bool gguf = suite.model_format == "gguf";
            if (suite.token_count != TokenCount && (!gguf || suite.token_count != MaximumTokenCount))
                throw new InvalidDataException("token_countは256、GGUFでは512も指定できます。");
            if (suite.model_id != "smoke" && suite.model_id != "AnkitAI/TinyJev-0.6B")
                throw new InvalidDataException("model_idはsmokeまたはAnkitAI/TinyJev-0.6Bを指定してください。");
            if (string.IsNullOrWhiteSpace(suite.model_revision) || suite.model_sha256 == null ||
                suite.model_sha256.Length != 64 || !Array.TrueForAll(suite.model_sha256.ToCharArray(), Uri.IsHexDigit))
                throw new InvalidDataException("モデルのrevisionと64桁のmodel_sha256が必要です。");
            if (!string.IsNullOrEmpty(suite.model_format) && suite.model_format != "onnx" && suite.model_format != "gguf")
                throw new InvalidDataException("model_formatはonnxまたはggufを指定してください。");
            if (suite.model_format == "gguf") GgufInputs.ResolveHeadPath("cases.json", suite);
            if (suite.cases == null || suite.cases.Length < 2)
                throw new InvalidDataException("比較するケースを2件以上指定してください。");

            var caseIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in suite.cases)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.case_id) ||
                    string.IsNullOrWhiteSpace(item.state_id) || !caseIds.Add(item.case_id))
                    throw new InvalidDataException("各ケースには一意なcase_idとstate_idが必要です。");
                int choiceCount = item.choice_ids == null ? 0 : item.choice_ids.Length;
                if (choiceCount < ChoiceCount || choiceCount > (gguf ? MaximumChoiceCount : ChoiceCount) ||
                    Array.Exists(item.choice_ids, string.IsNullOrWhiteSpace) ||
                    new HashSet<string>(item.choice_ids, StringComparer.Ordinal).Count != choiceCount)
                    throw new InvalidDataException(item.case_id + ": choice_idsは4件、GGUFでは4から8件の異なるIDが必要です。");
                if (item.input_ids == null || item.input_ids.Length != suite.token_count ||
                    Array.Exists(item.input_ids, value => value < 0) ||
                    item.attention_mask == null || item.attention_mask.Length != suite.token_count ||
                    Array.Exists(item.attention_mask, value => value != 0 && value != 1))
                    throw new InvalidDataException(item.case_id + ": input_idsとattention_maskはtoken_countと同じ長さが必要です。");
                if (item.option_positions == null || item.option_positions.Length != choiceCount ||
                    new HashSet<int>(item.option_positions).Count != choiceCount ||
                    item.decision_position == null || item.decision_position.Length != 1)
                    throw new InvalidDataException(item.case_id + ": option_positionsは候補数、decision_positionは1件が必要です。");
                foreach (var position in item.option_positions)
                    ValidatePosition(item, position, suite.token_count);
                ValidatePosition(item, item.decision_position[0], suite.token_count);
                if (item.reference_probabilities != null)
                    ValidateProbabilities(item.reference_probabilities, choiceCount);
            }
        }

        public static void ValidateProbabilities(float[] values, int expectedCount = ChoiceCount)
        {
            if (expectedCount < ChoiceCount || expectedCount > MaximumChoiceCount || values == null || values.Length != expectedCount)
                throw new InvalidDataException("確率は指定された4から8件の候補数と同じ長さが必要です。");
            double sum = 0;
            foreach (var value in values)
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1)
                    throw new InvalidDataException("確率には0から1の有限値を指定してください。");
                sum += value;
            }
            if (Math.Abs(sum - 1) > 1e-4)
                throw new InvalidDataException("確率の合計は1である必要があります。");
        }

        private static void ValidatePosition(PreparedCase item, int position, int tokenCount)
        {
            if (position < 0 || position >= tokenCount || item.attention_mask[position] != 1)
                throw new InvalidDataException(item.case_id + ": 位置はattention_maskが1のtokenを指す必要があります。");
        }
    }
}
