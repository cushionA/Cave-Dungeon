using System;
using System.Threading;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace JevValidation
{
    public sealed class JevValidationWindow : EditorWindow
    {
        private enum InferenceBackend { CPU, GPUCompute }

        [SerializeField] private ModelAsset _model;
        [SerializeField] private string _casePath = "Assets/PreparedCases/smoke.json";
        [SerializeField] private InferenceBackend _backend;
        private CancellationTokenSource _cancellation;
        private ValidationReport _report;
        private Vector2 _scroll;
        private bool _running;
        private string _error;

        [MenuItem("Tools/AI/Jev Validation")]
        public static void Open()
        {
            GetWindow<JevValidationWindow>("Jev Validation").minSize = new Vector2(760, 520);
        }

        private void OnEnable()
        {
            if (_model == null)
                _model = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/smoke.onnx");
        }

        private void OnDisable() => _cancellation?.Cancel();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Jev判断モデルの検証", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_running))
            {
                EditorGUI.BeginChangeCheck();
                _model = (ModelAsset)EditorGUILayout.ObjectField("ONNXモデル", _model, typeof(ModelAsset), false);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _casePath = EditorGUILayout.TextField("準備済みケースJSON", _casePath);
                    if (GUILayout.Button("選択", GUILayout.Width(60)))
                    {
                        var selected = EditorUtility.OpenFilePanel("準備済みケースJSON", "", "json");
                        if (!string.IsNullOrEmpty(selected))
                            _casePath = selected;
                    }
                }
                _backend = (InferenceBackend)EditorGUILayout.EnumPopup("推論バックエンド", _backend);
                if (EditorGUI.EndChangeCheck())
                    _report = null;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_running || _model == null || string.IsNullOrWhiteSpace(_casePath)))
                    if (GUILayout.Button("実行"))
                        Run();
                using (new EditorGUI.DisabledScope(!_running))
                    if (GUILayout.Button("中断"))
                        _cancellation?.Cancel();
                using (new EditorGUI.DisabledScope(_report == null || _running))
                    if (GUILayout.Button("JSONレポート保存"))
                        SaveReport();
            }

            if (_running)
                EditorGUILayout.HelpBox("推論中。最初のケースでウォームアップします。", MessageType.Info);
            if (!string.IsNullOrEmpty(_error))
                EditorGUILayout.HelpBox(_error, MessageType.Error);
            if (_report == null)
                return;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField(_report.model_id, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("モデル種別 / backend", _report.model_kind + " / " + _report.backend);
            EditorGUILayout.LabelField("revision", _report.model_revision);
            EditorGUILayout.LabelField("ONNX SHA256", _report.model_sha256);
            foreach (var result in _report.cases)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(result.case_id + " / " + result.state_id, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("選択 / 推論＋readback", result.chosen_id + " / " + result.inference_ms.ToString("F3") + " ms");
                    for (var index = 0; index < result.choice_ids.Length; index++)
                        EditorGUILayout.LabelField(result.choice_ids[index], result.probabilities[index].ToString("F6"));
                    EditorGUILayout.LabelField("参照確率との差", result.has_reference
                        ? "最大 " + result.max_reference_error.ToString("G5") + " / 平均 " + result.mean_reference_error.ToString("G5")
                        : "参照値なし");
                }
            }
            EditorGUILayout.LabelField("状態変更への感度", EditorStyles.boldLabel);
            foreach (var comparison in _report.state_comparisons)
                EditorGUILayout.LabelField(comparison.from_case_id + " → " + comparison.to_case_id,
                    "確率分布差(TV) " + comparison.probability_total_variation.ToString("F6") +
                    " / 選択変更 " + comparison.choice_changed);
            EditorGUILayout.EndScrollView();
        }

        private async void Run()
        {
            _running = true;
            _report = null;
            _error = null;
            _cancellation = new CancellationTokenSource();
            try
            {
                var suite = PreparedCaseFile.Read(_casePath);
                _report = await SentisCaseRunner.RunAsync(_model, suite,
                    _backend == InferenceBackend.CPU ? BackendType.CPU : BackendType.GPUCompute, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                _error = "中断しました。";
            }
            catch (Exception exception)
            {
                _error = exception.Message;
            }
            finally
            {
                _cancellation.Dispose();
                _cancellation = null;
                _running = false;
                if (this != null)
                    Repaint();
            }
        }

        private void SaveReport()
        {
            var path = EditorUtility.SaveFilePanel("検証レポート保存", "", "jev-validation.json", "json");
            if (!string.IsNullOrEmpty(path))
                _report.Save(path);
        }
    }
}
