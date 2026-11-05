using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace JevValidation
{
    [Serializable]
    public sealed class ValidationReport
    {
        public int schema_version = 1;
        public string model_id;
        public string model_revision;
        public string model_sha256;
        public string model_kind;
        public string backend;
        public string model_asset_path;
        public string created_utc;
        public string timing_scope;
        public CaseResult[] cases;
        public StateComparison[] state_comparisons;

        public void Save(string path)
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, JsonUtility.ToJson(this, true));
        }
    }

    [Serializable]
    public sealed class CaseResult
    {
        public string case_id;
        public string state_id;
        public string[] choice_ids;
        public float[] probabilities;
        public string chosen_id;
        public double inference_ms;
        public bool has_reference;
        public float[] reference_probabilities;
        public float max_reference_error;
        public float mean_reference_error;
    }

    [Serializable]
    public sealed class StateComparison
    {
        public string from_case_id;
        public string to_case_id;
        public float probability_total_variation;
        public bool choice_changed;
        public bool has_reference;
        public float reference_total_variation;
    }

    public static class ReportBuilder
    {
        public static ValidationReport Build(PreparedSuite suite, string backend, string modelPath,
            float[][] probabilities, double[] milliseconds)
        {
            var report = new ValidationReport
            {
                model_id = suite.model_id,
                model_revision = suite.model_revision,
                model_sha256 = suite.model_sha256,
                model_kind = suite.model_id == "smoke" ? "smoke" : "real",
                backend = backend,
                model_asset_path = modelPath,
                created_utc = DateTime.UtcNow.ToString("O"),
                timing_scope = "Schedule through completed readback; model load, tensor creation and one warm-up excluded.",
                cases = new CaseResult[suite.cases.Length]
            };
            for (var index = 0; index < suite.cases.Length; index++)
            {
                var item = suite.cases[index];
                var values = probabilities[index];
                var selected = 0;
                var result = new CaseResult
                {
                    case_id = item.case_id,
                    state_id = item.state_id,
                    choice_ids = item.choice_ids,
                    probabilities = values,
                    inference_ms = milliseconds[index],
                    has_reference = item.reference_probabilities != null,
                    reference_probabilities = item.reference_probabilities
                };
                for (var choice = 0; choice < values.Length; choice++)
                {
                    if (values[choice] > values[selected])
                        selected = choice;
                    if (!result.has_reference)
                        continue;
                    var error = Math.Abs(values[choice] - item.reference_probabilities[choice]);
                    result.max_reference_error = Math.Max(result.max_reference_error, error);
                    result.mean_reference_error += error / PreparedCaseFile.ChoiceCount;
                }
                result.chosen_id = item.choice_ids[selected];
                report.cases[index] = result;
            }

            var comparisons = new List<StateComparison>();
            for (var first = 0; first < report.cases.Length; first++)
            for (var second = first + 1; second < report.cases.Length; second++)
            {
                var from = report.cases[first];
                var to = report.cases[second];
                if (from.state_id == to.state_id ||
                    !new HashSet<string>(from.choice_ids, StringComparer.Ordinal).SetEquals(to.choice_ids))
                    continue;
                var comparison = new StateComparison
                {
                    from_case_id = from.case_id,
                    to_case_id = to.case_id,
                    choice_changed = from.chosen_id != to.chosen_id,
                    has_reference = from.has_reference && to.has_reference
                };
                for (var choice = 0; choice < from.choice_ids.Length; choice++)
                {
                    var aligned = Array.IndexOf(to.choice_ids, from.choice_ids[choice]);
                    comparison.probability_total_variation += .5f * Math.Abs(from.probabilities[choice] - to.probabilities[aligned]);
                    if (comparison.has_reference)
                        comparison.reference_total_variation += .5f * Math.Abs(from.reference_probabilities[choice] - to.reference_probabilities[aligned]);
                }
                comparisons.Add(comparison);
            }
            report.state_comparisons = comparisons.ToArray();
            return report;
        }
    }
}
