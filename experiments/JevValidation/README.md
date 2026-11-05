# Jev Validation

Unity 6000.6.8f1とSentis 2.6.1で、Jev系判断モデルのONNX推論を検証する独立プロジェクト。SisterGame本体や有料アセットへの依存はない。

`com.unity.pipeline`は公開されている`0.10.0-exp.1`を使用する。モデル・ケースの準備は`Python/`の手順に従う。

## 使い方

1. Unityでこのフォルダを開く。
2. `Tools > AI > Jev Validation`を開く。
3. `Assets/Models/smoke.onnx`と`Assets/PreparedCases/smoke.json`を選択し、CPUで実行する。
4. 各候補の確率、参照確率との差、状態変更時の分布差を確認し、JSONレポートを保存する。
5. 対応GPUでは`GPUCompute`でも同じケースを実行できる。

smokeの`model_id`は厳密に`smoke`。実TinyJevは`AnkitAI/TinyJev-0.6B`と固定revisionを記録する。選択したONNXのSHA256がケースJSONと一致しない場合は実行しない。

実モデルを使う場合は、Pythonから生成した`tinyjev.onnx`を`Assets/Models/TinyJev.onnx`、`cases.json`を`Assets/PreparedCases/TinyJev.json`へコピーしてimportする。これらの大きな実モデル成果物はGitの対象外。生のsafetensorsではなく、準備処理でexportしたONNXを選択する。

`Python/`での準備コマンド:

```powershell
python -m venv .venv
.venv/Scripts/python.exe -m pip install -r requirements.txt
.venv/Scripts/python.exe prepare.py smoke --output Artifacts/smoke
.venv/Scripts/python.exe prepare.py real --output Artifacts/tinyjev --device cuda
.venv/Scripts/python.exe prepare.py cases --model Artifacts/tinyjev/tinyjev.onnx --output Artifacts/tinyjev --device cuda
```

CUDAを使わない場合は`--device cpu`を指定する。`cases`は既存ONNXを使って入力と参照値を更新する。`--states states.json`で`[{"state_id":"...","state":"..."}, ...]`形式の2状態以上を指定できる。

## 入出力契約

`schema_version=1`。ルートには`model_id`、`model_revision`、`model_sha256`、`token_count=256`、`cases`を持つ。

| モデル入力 | 型・shape | JSON内の配列 |
|---|---|---|
| `input_ids` | int32 `[1,256]` | 256要素 |
| `attention_mask` | int32 `[1,256]` | 256要素、0または1 |
| `option_positions` | int32 `[4]` | 有効token位置4件 |
| `decision_position` | int32 `[1]` | 有効token位置1件 |
| 出力`probabilities` | float32 `[4]` | 4候補の確率 |

各ケースには`case_id`、`state_id`、`choice_ids`4件と上記入力が必要。`state`は任意の説明、`reference_probabilities`は任意の4要素の参照確率。参照値なしは差0として評価しない。確率は有限・非負で、合計が1になることを検証する。

同じ候補集合を持つ異なる`state_id`のケース間で、候補IDを揃えて全変動距離(TV)を計算する。0は同じ分布、1は重なりのない分布。候補の選択が変わったかも記録する。これらは準備した状態対への応答を測る指標であり、ゲームプレイ成績は別途評価する。

実モデルの標準ケースは4件。`verbal_*`の対は攻撃・撤退の助言を含む説明文への応答確認で、`numeric_hp90`と`numeric_hp5`の対は同じ文面のHPだけを90/100から5/100へ変えた確認となる。両者の感度は区別して評価する。Python側では説明文の対よりHPだけの対の確率変化が小さく、ゲーム用AIとしての判断品質は未評価。

推論時間は`Schedule`から出力readback完了まで。モデル読込・Tensor作成と、最初の1ケースを使うウォームアップは含めない。

## テスト

EditModeの`JevValidation.Editor.Tests`には入力検証・参照差・感度計算・CPU/GPUのsmoke実推論を検証する5テストを置く。GPUCompute未対応の環境ではGPUテストを明示的にskipする。

GUIを使わず起動する場合:

```powershell
unity open "<このフォルダの絶対パス>" --batchmode --until-ready
unity command test run --mode editor --filter JevValidation.Editor.Tests --filter-type assembly --project-path "<このフォルダの絶対パス>" --format json
```

GPU検証では`-nographics`を付けない。CPUだけの環境ではCPU結果を使う。0件実行は検証完了として扱わない。

2026-11-06のUnityネイティブ実行では5件中5件成功、skipなし。CPUとGPUComputeの両方でsmokeの実推論を確認した。

実TinyJevはPython側のONNX Runtimeと上流モデルの4ケース比較に成功し、最大確率誤差は約0.001217だった。1,194,969,111バイトの単一ONNXをUnityへimportした際、本体Editorなどを並行稼働した32GB環境で2,387,182,664バイトの割当に失敗し、asset書込みエラーとなった。実モデルのSentis推論・同等性・所要時間は未検証。smoke成功と実モデル実行成功は分けて扱う。

自動再importを避けるため、実モデルのコピーは`Assets/`から外している。原本は`Python/Artifacts/tinyjev/`に残る。

## 量子化の検討

Sentis 2.6.1はONNXの重みを32bitへ変換するため、FP16のONNXでもimport後のメモリが半分になるとは限らない。[公式の量子化API](https://docs.unity3d.com/Packages/com.unity.ai.inference@2.6/manual/quantize-a-model.html)は読込済みモデルをFloat16またはUint8にして保存する方式。インストール済み2.6.1のONNX importerには読込前の量子化設定がなく、このAPIだけでは今回のimport時ピークを避けられない。量子化後も演算時に高精度へ戻すため、速度改善は実測が必要。

一般的なONNX量子化で使うQuantizeLinear、DequantizeLinear、MatMulInteger、QLinearMatMulは[Sentis 2.6.1の未対応演算](https://docs.unity3d.com/Packages/com.unity.ai.inference@2.6/manual/supported-operators.html#unsupported-operators)。[ONNX RuntimeのINT8量子化](https://onnxruntime.ai/docs/performance/model-optimizations/quantization.html)はPython側で4ケースの確率誤差・選択・HP変更への応答とメモリを比較する候補になるが、その成果物をそのままSentisへ渡せるわけではない。

Unity内で軽量な判断を動かす次の候補は、より小さいモデルまたはTinyJevの出力を教師にした4候補の判定モデル。この場合も固定ケースと参照値を保存し、smokeと同じCPU/GPU測定に加えて判断品質を別に検証する。量子化版・小型モデルの実装と性能評価はまだ行っていない。

通常のLLMを比較する場合は、llama.cpp系の[LLM for Unity](https://github.com/undreamai/LLMUnity)でGGUFを扱う経路もある。同規模の[Qwen3-0.6Bの配布例](https://huggingface.co/unsloth/Qwen3-0.6B-GGUF/tree/main)はQ4_K_Mが約397MB、Q8_0が約639MB。これらは重みのサイズで、実行時メモリは別途必要になる。TinyJevの専用pointer headまで同バックエンドでそのまま動くことは確認しておらず、GGUF対応とJev互換の実行を区別する。
