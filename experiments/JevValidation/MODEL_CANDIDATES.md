# 仲間AI向け有望モデル一覧

更新日：2026-11-14。SAIFの2026-11-10測定までを反映。

**SAIF 0.8B Lightningを、次の小さな判断課題の検証候補として残す。** 日本語2課題の観測正解率は比較した7実装で最も高かったが、既存上位への明確な優位は確認できていない。指揮を直接任せるモデルの採用判断は保留する。

Hindsight・Unee系は判断品質の比較基準、TinyJevはUnity組み込み経路の比較基準として残す。今回は非商用検証を前提とし、SAIFも候補に含める。

## 同じ問題で比較した候補

日本語の記憶関連判定500場面、提示済み作戦の4択500場面を各1回評価した。英語の対応訳は各100件の副評価として別集計している。実行失敗も誤答に含める採点で、下表の7実装では各1,200試行の実行失敗は0件だった。

| モデル | 記憶の関連性判定 | 作戦4択 | 次の扱い |
|---|---:|---:|---|
| [SAIF 0.8B Lightning](https://huggingface.co/GyrTechnology/saif-0.8b-lightning) | **282/500＝56.4%** | **153/500＝30.6%** | 選択専用モデルの追加検証候補。非商用 |
| [Hindsight hs-decide-1-flash 0.8B](https://huggingface.co/hindsight-ai/hs-decide-1-flash) | 279/500＝55.8% | 129/500＝25.8% | 記憶判定の比較基準。不要情報の採用が多い |
| [Unee 0.8B](https://huggingface.co/uneeverse/unee-0.8b) | 278/500＝55.6% | 131/500＝26.2% | 小型モデルの比較基準 |
| [Unee 2B](https://huggingface.co/uneeverse/unee-2b) | 269/500＝53.8% | 140/500＝28.0% | サイズを増やす効果の比較基準 |
| [TinyJev 0.6B](https://huggingface.co/AnkitAI/TinyJev-0.6B) | 254/500＝50.8% | 120/500＝24.0% | Unity・分割Sentisの動作確認用に維持 |
| [LFM2.5 1.2B JP 202606](https://huggingface.co/LiquidAI/LFM2.5-1.2B-JP-202606) | 250/500＝50.0% | 115/500＝23.0% | 今回の条件では優先度を下げる。記憶は全件true |
| [LFM3 1.7B fast](https://huggingface.co/LiquidAI/LFM3-1.7B) | 250/500＝50.0% | 125/500＝25.0% | 今回の条件では優先度を下げる。記憶は全件false、作戦は全件先頭 |

モデルごとに公式入力形式・head・精度・実行方法が異なるパイプラインの比較である。固定した合成課題の結果から、実ゲームの強さやモデル一般の性能順位は決めない。従来2モデルの比較は[拡張検証](Results/expanded-model-statistics.json)、追加4モデルは[追加候補レポート](Results/additional-candidates-comparison.html)、7実装の比較は[SAIFレポート](Results/saif-comparison.html)に保存している。

## SAIFの評価と実行条件

日本語の95% Wilson区間は、記憶52.0〜60.7%、作戦26.7〜34.8%。既存の課題別最高値との差は、記憶でHindsightより+0.6ポイント（対応95%区間−6.9〜+8.2）、作戦でUnee 2Bより+2.6ポイント（−4.2〜+10.0）だった。これらは探索的な比較ごとの区間で、多重比較補正は行っていない。

作戦では**308/500＝61.6%が明示された実行条件に違反**した。別枠の単純な事実確認は8/8正解だったが、この8問から一般的な精度は推定しない。候補の実行可能性をモデルだけに任せる根拠は得られていない。

| 項目 | 確認結果 |
|---|---|
| モデル | GyrTechnology / saif-0.8b-lightning v0.3。[固定revision](https://huggingface.co/GyrTechnology/saif-0.8b-lightning/tree/0cc7d0954a7aa48e0ba7302c4839185eb5aa164d) |
| 推論経路 | 公式の専用decision engine。BF16 decoder・FP32 head、無量子化。公開temperatureを維持 |
| CPU時間 | 4スレッド、主1,200試行の中央値約1.25秒／回、95パーセンタイル約1.76秒。初期化・warmupを除外 |
| メモリ | workerプロセスの観測VmHWM約1.94 GiB。初期化・warmupも含む |
| 実行環境 | Linux、Torch 2.9.1+cpu、Transformers 5.17.0。CPU reference fallbackを使用 |
| Unity・Sentis | このモデルでは未検証。CPU時間をUnity上の性能として扱わない |
| ライセンス | [CC-BY-NC-4.0](https://huggingface.co/GyrTechnology/saif-0.8b-lightning/blob/0cc7d0954a7aa48e0ba7302c4839185eb5aa164d/LICENSE)。今回の非商用検証の対象 |

過去モデルとはCPU配分や実行精度にも違いがあり、この時間だけで速度の優劣は決めない。作者公表の18msはGPU測定で、上表のCPU測定とは条件が異なる。詳細条件・全6比較の対応区間は[比較レポート](Results/saif-comparison.html)、各ケースの結果は[CSV](Results/saif-model-outcomes.csv)、入力固定と来歴は[プロトコル](Results/saif-benchmark-protocol.json)と[成果物manifest](Results/saif-artifact-manifest.json)を参照。

## 別課題で検証した候補

以下は上の1,200入力と異なる課題・評価方法である。

| モデル | 検証結果 | 次の扱い |
|---|---|---|
| [Needle 3](https://huggingface.co/Cactus-Compute/needle3) | 測定したのは約121M版。日本語の実行可能指示72件で正しい行動は1件。確認が必要な24件のうち3件で行動を出した | 自然文から現在の8行動への変換は保留。公式の抑制候補を強制採用しない |
| [Intern-Decision 0.8B](https://huggingface.co/internlm/Intern-Decision-0.8B) | 正解を定義していない18状態・8作戦で反応を確認。Unity CPUの判断位置取得経路でHF参照と18/18の選択一致、平均7.960秒／判断 | 性格・目的入力とUnity経路の比較対象。正解率の評価は今後 |
| [Intern-Decision 2B](https://huggingface.co/internlm/Intern-Decision-2B) | 同じ18状態を公式HF CPU経路で実行。正解率は未定義、平均3.728秒／判断。Unityは未検証 | 大きいモデルの比較対象。優先採用の根拠は未取得 |
| [DeBERTa v3 xsmall zeroshot](https://huggingface.co/MoritzLaurer/deberta-v3-xsmall-zeroshot-v1.1-all-33) / [NLI MiniLM2 L6](https://huggingface.co/cross-encoder/nli-MiniLM2-L6-H768) | ONNX Runtime CPUで18状態の8候補を比較。選択候補のentailment確率は約2〜7% | 現在の仮説文・実行条件では置き換えを保留 |

Needleの根拠は[拡張検証の統計](Results/expanded-model-statistics.json)、Intern-Decisionは[追試結果](Results/intern-decision-linux.json)、Unity経路・NLI比較を含む過去の手順は[検証README](README.md)を参照。

## 次に確かめる用途

次の候補は、ゲーム側で実行可能な作戦を絞り、その中からキャラクターの作戦傾向を1つ選ばせる小課題。SAIFを含む候補で、条件分岐だけの実装に対して何が改善するかを確かめる。CPUで秒単位の推論になるため、まずは局面変化時の低頻度判断を対象にする。

キャラクターらしい戦況の解釈、士気の変化、敵の行動傾向の分析は、この記憶・作戦4択の測定では評価していない。新しい用途の評価結果は、この一覧の実測値と分けて追記する。
