# Jev作戦選択の検証

Unity 6000.6.8f1で、TinyJevの学習済みbackboneと元のJev headをLLMUnityで実行する独立プロジェクト。SisterGame本体・有料アセットへの依存はない。Built-inを使用する。

Jevの担当は上位作戦の選択・切替。誘導から待ち伏せ・攻撃までの各段階は、選んだ作戦に対応する行動プリセットやMLモデル側が実行する。目的・個性は外部で保持して判断入力に含め、推論中も現在の行動を継続する運用を想定する。ゲーム本体への接続は未実装。

## 準備

このフォルダを作業ディレクトリにする。Pythonの依存は専用venvへ導入する。

```powershell
python -m venv Python/.venv
Python/.venv/Scripts/python.exe -m pip install -r Python/requirements.txt
Python/.venv/Scripts/python.exe Python/prepare.py real --output Python/Artifacts/tinyjev --device cuda
Python/.venv/Scripts/python.exe Python/prepare_gguf.py prepare --threads 4
Python/.venv/Scripts/python.exe Python/prepare_strategies.py --device cuda
```

CUDAを使わない場合は`--device cpu`。モデルは[AnkitAI/TinyJev-0.6B](https://huggingface.co/AnkitAI/TinyJev-0.6B)、revision `c559c2f7ea95069f92af8b999f512345bbb87d22`へ固定し、checkpointの6ファイルをハッシュ検証する。汎用Qwenの会話生成へ置き換えない。

`prepare.py real`は上流参照とONNX比較も生成する。ONNXをUnityのAssetsへコピーする必要はない。`Python/Artifacts/gguf/{F16,Q8_0,Q4_K_M}/`へGGUF・元FP32の`tinyjev.head`・ケースを生成し、この場所から直接選択する。モデル・venv・native bundleはGit対象外。

GGUFの再利用は`.gguf.provenance.json`と元checkpoint・変換tool・親F16・成果物のSHAを照合する。既存モデルを検証し直す場合は`Python/prepare_gguf.py verify-existing`を明示する。別ファイルへの再生成結果とSHAが完全一致した場合だけ検証情報を付ける。

UPMはLLMUnity 3.0.3のcommit `9931a9ea46906b1f43adaf9498c05ccf227b5d0b`、Pipeline `0.10.0-exp.1`、Sentis `2.6.1`へ固定。同梱LlamaLibは2.0.5、llama.cppは`872646b30c342c5fa98efec002f94fdb4f1f1ea5`。初回importで公式native bundle約1.82GBをディスクへ取得・展開する。

## Unityで実行

1. このフォルダをUnityで開き、`Tools > AI > Jev Validation`を開く。
2. `LLMUnityGPU`を選び、`Python/Artifacts/gguf/Q8_0/tinyjev-Q8_0.gguf`と同じフォルダの`cases-strategies.json`を選ぶ。
3. GPU offload層数は`99`で全層を要求する。使用率を%で指定する設定ではない。CPUは`LLMUnityCPU`を明示選択できる。
4. 各ケースの作戦ID・候補確率・参照との差・入力変更時の分布差を確認し、JSONを保存する。

`chosen_id`が選ばれた作戦ID。作戦IDと行動プリセットやMLパラメータの対応は、接続先の設定で管理する。

ヘッドレスでも動作する。GPUComputeのsmokeを実行する場合は`-nographics`を付けない。

```powershell
unity open "<このフォルダの絶対パス>" --batchmode --until-ready
unity command test run --mode editor --filter JevValidation.Editor.Tests --filter-type assembly --project-path "<このフォルダの絶対パス>" --format json
unity command script eval --code 'var r = await JevValidation.LlmUnityCaseRunner.RunAsync("Python/Artifacts/gguf/Q8_0/tinyjev-Q8_0.gguf", "Python/Artifacts/gguf/Q8_0/cases-strategies.json", default, JevValidation.LlmUnityBackend.GPU, 99); r.Save("Artifacts/strategy-Q8-GPU.json"); return new { r.backend, r.offloaded_gpu_layers, count = r.cases.Length };' --timeout 600 --project-path "<このフォルダの絶対パス>" --pid <専用EditorのPID> --format json
```

WindowsのGPU経路は公式cuBLAS backendを選び、actual offload層数とCUDA bufferを確認する。nativeログにはINFOを取得する`Debug(5)`／`--log-verbosity 3`を使う。サービスが生存中にcallbackを解除し、native終了までmanaged delegateを保持してから設定・排他を解放する。

## 作戦と比較ケース

作戦候補は次の8種類。個々の技や移動コマンドは生成しない。

| ID | 作戦 |
|---|---|
| direct_assault | 正面から攻勢を取る |
| lure_ambush | 囮・誘導・待ち伏せ・攻撃を一連のプリセットで行う |
| flank | 側面から有利な位置を取る |
| guard_ally | 味方を防護する |
| ranged_pressure | 遠距離から圧力をかける |
| observe | 状況を観察する |
| disengage | 離脱して立て直す |
| bypass | 交戦を回避して進む |

18ケースには、同じ戦況で性格3種類×目的2種類、同じ性格・目的で戦況だけを変えた3件、現在の作戦だけを変えた対照、候補順を変えた対照、作戦未設定の性格3種類×目的2種類を含める。目標の作戦を選ぶような指示は追加していない。

準備済みの自然文・token・候補順・参照値を保存し、候補本文もsnapshotとSHAで固定する。候補本文を変えた場合は参照を再生成して比較条件を揃える。`cases.json`の既存6ケース、元ONNX、3種類のGGUFとheadは作戦ケース生成時に変更しない。

上流PyTorch参照の作戦未設定時の結果:

| 性格 | 味方保護 | 脱出 |
|---|---|---|
| 狡猾 | lure_ambush 30.30% | lure_ambush 26.46% |
| 好戦的 | direct_assault 33.90% | disengage 28.18% |
| 慎重 | guard_ally 23.21% | disengage 37.19% |

性格・目的による選択差が出た。一方、現在の作戦をlure_ambushにした元9ケースはすべて同作戦を維持した。同じ戦況で現在の作戦だけをdirect_assault／observeへ変えると、それぞれが選ばれたため、現在作戦への依存が強い。候補順変更でも、ID整列後の最大確率差は約0.0382だった。

UnityのQ8 GPUでも8候補・512contextの全18ケースを実行し、上流と18/18で選択が一致した。平均1093.128ms／状態、最大確率差0.016404、全153組の状態比較を記録。上記の個性・目的による選択差も維持した。旧4ケースの速度比較とは入力長と候補数が異なるため分けて扱う。

これらは固定入力への反応を示す。作戦の新規評価と維持判定を分ける入力形式や、戦況急変時の切替は継続評価する。内部で先読み計画や永続記憶を持つこと、ゲーム内で目的を達成できることは、この比較からは判定しない。

## 代替モデルの比較

```powershell
Python/.venv/Scripts/python.exe Python/compare_strategies.py download
Python/.venv/Scripts/python.exe Python/compare_strategies.py run
```

同じ18ケースの自然文と保存済みの8候補本文を使い、NLIで候補を評価する。仮説文は比較前に固定し、望む結果になるよう調整していない。モデル・tokenizerのrevisionとLFS SHA／Git blobを固定・検証する。

| モデル | ONNX容量 | CPU平均ms／8候補 |
|---|---:|---:|
| [DeBERTa v3 xsmall zeroshot](https://huggingface.co/MoritzLaurer/deberta-v3-xsmall-zeroshot-v1.1-all-33) | 87.246MB | 510.116 |
| [NLI MiniLM2 L6](https://huggingface.co/cross-encoder/nli-MiniLM2-L6-H768) | 82.823MB | 496.688 |

CPU 4 threads、1ウォームアップ後、18ケースを各1回測定。load・tokenization・warmup・推論を分けて記録する。最大入力長は158／167tokenで上限512以内、切り詰めなし。Unityへは導入せずPythonのONNX Runtimeで比較した。

相対rankと、各候補のNLI class確率・raw logitsを別に保存する。相対rankはゲームの成功確率ではない。選択候補のentailment確率は約2〜7%で、現条件ではJevを置き換える根拠は得られていない。感情分析モデルへの固定はせず、作戦選択に対する適性を評価する。

結果は`Python/Artifacts/alternatives/`、元の参照は`Python/Artifacts/strategies/`へ保存する。

## GGUFと入力の契約

`schema_version=1`。モデルのrevision・SHA、head basename・SHA、量子化形式を持つ。モデルやheadがSHA不一致なら実行しない。

| 経路 | token_count | 候補数 |
|---|---:|---:|
| LLMUnity GGUF | 256または512 | 4〜8 |
| Sentis ONNX smoke | 256 | 4 |

各ケースの`choice_ids`と`option_positions`・参照確率の長さは同じ候補数とする。`input_ids`と`attention_mask`はtoken_count要素、decision位置は1件。位置は有効tokenを指し、参照値なしは誤差0として扱わない。候補IDを揃えて状態間の全変動距離(TV)を計算する。

LLMUnityは右paddingだけを除いた既存token IDsを、同一プロセスの`127.0.0.1`のraw `/embeddings`へ送る。BOS・chat template・L2正規化を追加せず、pooling none・因果マスク・最終RMSNorm後の全有効token行×1024要素を検証する。指定候補とdecision位置へ、元FP32 headの投影・temperature付き内積・softmaxを適用する。

headはASCII8バイト`JEVHEAD1`、Int32 little endianのhidden size=1024／head dimension=256、Float32のtemperatureとq_weight・q_bias・k_weight・k_bias。FP32 row-majorで2,099,220 bytes。

| GGUF | 容量MB（10⁶ bytes） | 既存CPU4ケースの上流参照との差・最大値 |
|---|---:|---:|
| F16 | 1198.18 | 0.001289 |
| Q8_0 | 639.44 | 0.005664 |
| Q4_K_M | 396.70 | 0.041503 |

元4ケースの選択は全形式で上流と一致した。Q4の確率差が大きいため、容量と選択品質を併せて評価する。各モデルを1つずつロードし、終了・破棄を待ってから次へ進む。

## 計測とテスト

測定環境はIntel Core i7-10750H @ 2.60GHz、RAM 31.8GiB、RTX 2070 SUPER 8GiB、Windows 11 Pro build 26200、Unity 6000.6.8f1のヘッドレスEditor。

`inference_ms`はHTTP要求から全token readback・JSON検証・head完了まで。`backbone_ms`はhead以外の区間、`head_ms`はhead計算。モデル起動時間と1ケースのwarmupを除く。受信IOはUnitySynchronizationContextへのchunkごとの復帰を避ける。旧IO処理を含んだ秒数はモデル本体の速度比較には使わない。

同じコード・Q8モデル・元4ケースで測定した結果:

| backend | 平均ms／ケース | 上流参照との選択一致 |
|---|---:|---:|
| CPU 4 threads | 1555.628 | 4/4 |
| GPU cuBLAS | 415.162 | 4/4 |

このGPU測定では29/29層のoffload、CUDA model buffer 604.15MiB、compute buffer 149.38MiBを確認した。GPUの参照確率との最大誤差は0.005119。旧IO処理と修正後CPUの出力確率は完全一致した。

VRAMの全体使用量はbaseline 1277MiBからpeak 2065MiBを観測した。desktop等の使用分を含む値で、WDDMのプロセス単独使用量は取得できなかった。上記CUDA buffer値とプロセス全体の使用量は分ける。

Python/から実行:

```powershell
.venv/Scripts/python.exe -m unittest -v test_prepare.py test_prepare_gguf.py test_prepare_strategies.py test_compare_strategies.py
```

Pythonは21/21、Unity EditModeは17/17成功、skipなし。head・GGUF境界・GPU証拠・可変候補を検証し、既存Sentis CPU/GPUCompute smokeも含む。実モデルは元4ケースの同コードCPU/GPU比較、個性だけを変えた2ケース、作戦18ケースを別に実行した。個性2ケースでは攻撃確率が約82.54%→73.31%、退避が約4.31%→8.83%へ変化し、最大候補は双方attackだった。0件実行は成功と数えない。

キャンセルは応答待機を止め、native処理の終了とモデル破棄・排他解放を待つ。CPU実モデルでは5秒後のキャンセル要求から約9秒で後処理完了を確認した。即時のnative停止は保証しない。

## Sentisの比較範囲と残件

Sentis CPU/GPUComputeは同梱`Assets/Models/smoke.onnx`と`Assets/PreparedCases/smoke.json`で確認できる。実TinyJevのONNXはPython側で上流との照合に成功したが、Unity importで約2.39GBの割当失敗があり、Assetsから外している。実モデルのSentis推論は未検証。

Sentis 2.6.1の[量子化API](https://docs.unity3d.com/Packages/com.unity.ai.inference@2.6/manual/quantize-a-model.html)は読込後のモデルを変換する方式。一般的なONNX INT8の演算は[未対応一覧](https://docs.unity3d.com/Packages/com.unity.ai.inference@2.6/manual/supported-operators.html#unsupported-operators)を確認する。

ゲーム本体への作戦ID接続、維持・切替条件、同時個体数、幅広い戦況での判断品質、追加学習の要否は[将来タスク](../../docs/FUTURE_TASKS.md)で追跡する。
