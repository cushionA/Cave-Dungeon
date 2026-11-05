using System;
using System.Threading;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace JevValidation
{
    public sealed class JevValidationWindow : EditorWindow
    {
        private enum InferenceBackend { LLMUnityCPU, SentisCPU, SentisGPUCompute, LLMUnityGPU }

        [SerializeField] private ModelAsset _model;
        [SerializeField] private string _casePath = "Assets/PreparedCases/smoke.json";
        [SerializeField] private string _ggufPath = "";
        [SerializeField] private string _ggufCasePath = "";
        [SerializeField] private InferenceBackend _backend = InferenceBackend.LLMUnityGPU;
        [SerializeField] private int _gpuLayers = 99;
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
                _backend = (InferenceBackend)EditorGUILayout.EnumPopup("推論バックエンド", _backend);
                if (_backend == InferenceBackend.LLMUnityCPU || _backend == InferenceBackend.LLMUnityGPU)
                {
                    _ggufPath = SelectPath("TinyJev GGUF", _ggufPath, "gguf");
                    _ggufCasePath = SelectPath("準備済みケースJSON", _ggufCasePath, "json");
                    if (_backend == InferenceBackend.LLMUnityGPU)
                    {
                        _gpuLayers = EditorGUILayout.IntField("GPU offload層数", _gpuLayers);
                        EditorGUILayout.HelpBox("GPUへ移す層数を指定します。99は全層を要求します。使用率の%指定はありません。", MessageType.Info);
                    }
                }
                else
                {
                    _model = (ModelAsset)EditorGUILayout.ObjectField("ONNXモデル", _model, typeof(ModelAsset), false);
                    _casePath = SelectPath("準備済みケースJSON", _casePath, "json");
                }
                if (EditorGUI.EndChangeCheck())
                    _report = null;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_running || ((_backend == InferenceBackend.LLMUnityCPU || _backend == InferenceBackend.LLMUnityGPU)
                           ? string.IsNullOrWhiteSpace(_ggufPath) || string.IsNullOrWhiteSpace(_ggufCasePath)
                           : _model == null || string.IsNullOrWhiteSpace(_casePath))))
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
            EditorGUILayout.LabelField("モデル SHA256", _report.model_sha256);
            if (_report.model_format == "gguf")
            {
                EditorGUILayout.LabelField("量子化 / head SHA256", _report.backbone_quantization + " / " + _report.head_sha256);
                EditorGUILayout.LabelField("モデル起動", _report.model_load_ms.ToString("F3") + " ms");
                EditorGUILayout.LabelField("GPU要求層数 / 実offload", _report.requested_gpu_layers + " / " + _report.offloaded_gpu_layers + "/" + _report.offloadable_gpu_layers);
                EditorGUILayout.LabelField("native backend", _report.native_architecture);
            }
            foreach (var result in _report.cases)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(result.case_id + " / " + result.state_id, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("選択 / 計測時間", result.chosen_id + " / " + result.inference_ms.ToString("F3") + " ms");
                    if (_report.model_format == "gguf")
                        EditorGUILayout.LabelField("backbone＋readback / head", result.backbone_ms.ToString("F3") + " / " + result.head_ms.ToString("F3") + " ms");
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
                if (_backend == InferenceBackend.LLMUnityCPU || _backend == InferenceBackend.LLMUnityGPU)
                    _report = await LlmUnityCaseRunner.RunAsync(_ggufPath, _ggufCasePath, _cancellation.Token,
                        _backend == InferenceBackend.LLMUnityGPU ? LlmUnityBackend.GPU : LlmUnityBackend.CPU, _gpuLayers);
                else
                    _report = await SentisCaseRunner.RunAsync(_model, PreparedCaseFile.Read(_casePath),
                        _backend == InferenceBackend.SentisCPU ? BackendType.CPU : BackendType.GPUCompute, _cancellation.Token);
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

        private static string SelectPath(string label, string path, string extension)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                path = EditorGUILayout.TextField(label, path);
                if (GUILayout.Button("選択", GUILayout.Width(60)))
                {
                    var selected = EditorUtility.OpenFilePanel(label, "", extension);
                    if (!string.IsNullOrEmpty(selected)) path = selected;
                }
                return path;
            }
        }
    }
}
