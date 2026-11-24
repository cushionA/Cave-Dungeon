---
date: 2026-11-24
session_topic: 騎士とAIのマルチロック魔術、飛翔体と範囲攻撃の予告
status: in-progress
branch: codex/ai-terrain-decision-dataflow
---

# 三体を狙う魔術を、入力から一発ごとの警告まで通す

## 現在地

最初の完成場面は、敵の術師がシスターを主標的にしつつ、騎士・シスター・もう一体の味方に一発ずつ放つ場面。騎士宛ての一発が飛んでくる側だけ、騎士の画面端に弧が出る。対応魔術を装備した騎士にも同じ発射処理を使う。選択・弾別の割当・予告の3経路は機能DBで計画段階にある。[ゲームデザイン](../../../designs/game-design.md)・[登録マニフェスト](../../../designs/call-paths.json)

**別の作業コピーに機能DBを復元する。** このブランチでは `designs/call-paths.json` が正本。`feature-log.db` はローカルで生成する。新しい作業コピーでは、リポジトリのルートから次をBashで実行すると、遠征・指揮の親機能とその11経路を取り込める。すでに親機能を登録済みなら、3行目のインポートから実行する。

```bash
python tools/feature-db.py init
python tools/feature-db.py add DungeonExpedition_AllyRoster --category system
python tools/feature-db.py import-call-paths --file <(python -c '
import json
from pathlib import Path
rows = json.loads(Path("designs/call-paths.json").read_text(encoding="utf-8"))
print(json.dumps([row for row in rows if row["feature"] == "DungeonExpedition_AllyRoster"]))
')
python tools/feature-db.py call-paths DungeonExpedition_AllyRoster
```

号令の `dispatch-faction-field-order` が `runtime_tested`、残り10経路が `planned` と出れば復元できている。以下の3経路はこの10件に含まれる。

**いま動く操作を確認する。** Unity 6000.6.8f1で `Assets/Scenes/CoreTestScene.unity` を開いて再生する。HUD左下に「集合」が表示される。Fまたは十字キー下を短く押して離すと発令し、0.35秒以上押して離すと次の枠「集中攻撃」に切り替わる。マウスで敵の体を指すか右スティックを敵に向け、同じボタンを短押しする。照準が有効なら味方に伝わり、空や遮蔽物なら選び直せる。これは現在の2D号令の基準になる。魔術側の入力は現状 `Player/Skill` のQ・右肩ボタンを押した時点で記録される。[試作シーン](../../../Assets/Scenes/CoreTestScene.unity)・[操作設定](../../../Assets/InputSystem_Actions.inputactions)・[号令の照準と受付](../../../Assets/MyAsset/Runtime/Input/PlayerInputHandler.FieldOrders.cs)

Unityテスト環境を設定したリポジトリのルートで、同じ入力と飛翔体の既存動作を再生できる。

```sh
UNITY_TEST_FILTER='PlayerFieldOrderInputTests;FieldOrderDeliveryTests' bash scripts/run-unity-tests.sh PlayMode
UNITY_TEST_FILTER=ProjectileTargetPolicyPlayTests bash scripts/run-unity-tests.sh PlayMode
```

1本目は入力5件・配送5件の計10件。集合の試験では、騎士の1.7メートル手前で静止しているシスターが、Fを離した次の判断tickで騎士へ動く。2本目は8件。追尾対象が無効になる試験では、弾が通常の候補検索で別の適格な相手へ進む。これは「その相手に明示ロックした」という通知の根拠には使えない。2026-11-24の全件実行はEditMode 2,246件、PlayMode 253件成功。次節の3D試作には個別の実行記録が必要になる。[入力の試験](../../../Assets/Tests/PlayMode/PlayerFieldOrderInputTests.cs)・[配送の試験](../../../Assets/Tests/PlayMode/FieldOrderDeliveryTests.cs)・[追尾・爆発の試験](../../../Assets/Tests/PlayMode/ProjectileTargetPolicyPlayTests.cs)・[テスト実行スクリプト](../../../scripts/run-unity-tests.sh)

Unityを開く前に、地点指定の範囲技で「狙った時点」と「実際に当たった時点」の違いも比較できる。次のコマンドでは12場面を候補の列挙順8通りで実行する。出力の `passed` は96、発射後に予測と着弾対象が異なる回数は24。`enemy_reused_after_release` では、同じ床に出現した別世代の敵も着弾時の範囲に入る。下の第2段階で作る個体指定の弾は、古い世代の印をその敵に引き継げば失敗とする。[比較実験](../../../tools/experiments/terrain_ai/multi_target.py)

```sh
python tools/experiments/terrain_ai/multi_target.py
```

## 次セッションでやること

### 1. 騎士が付けた印を、離すまで保持する

対応する魔術一つに絞る。`PlayerInputHandler.OnSkill` で既存の押下処理を確認し、該当する魔術のときだけ、開始・解放・中断を入力アクションから受け取る。プレイヤーが使うカメラで敵を画面座標に移し、画面内・射程内・射線が通る敵のうち、騎士から最も近い一体を最初の印にする。以降の距離は画面上の直前の印から測る。入力マップ変更やプレイヤー無効化による中断では印を消す。[押下の入口](../../../Assets/MyAsset/Runtime/Input/PlayerInputHandler.cs)・[世代付き個体参照](../../../Assets/MyAsset/Core/Common/ActorRef.cs)

**最初の失敗を再現する試験。** テスト用の配置で、騎士に最も近いAを画面中央、BをAの右上、CをBの右に置く。右肩ボタンを押したまま、右スティックを右上に倒して保持、中央に戻し、右に倒してから右肩を離す。期待する印の履歴は `A → A,B → A,B → A,B,C`、解除時の発射先はA・B・C各一発。現状のSkill押下では最初の印が付かず、この試験は最初の比較で失敗する。まず選択履歴を通し、次項の接続後に発射先まで通す。8方向それぞれについて、印なしの方向・選択済みの敵・上限三体・カメラ回転を試す。カメラの追跡先と号令で味方に伝えた敵も試験前後で照合する。

### 2. AIの一回の詠唱と、弾ごとの対象をつなぐ

敵術師と味方術師に同じ対応魔術を持たせる。AIの主標的を最初の候補とし、術者に見えて射程・射線を満たす別個体を詠唱単位で集める。実装の接続点は `GameActionRuntime.HandleExecution`。今は実行が `Active` に入ると、一つの `targetRef` を `ProjectileManager.SpawnSpreadFromExecution` へ渡し、散弾はその対象を共有する。対応魔術では、この時点で各候補の世代・生存・陣営・射線を再確認し、有効な個体ごとに一発を割り当てる。既存の実行ID・攻撃情報の確定処理を通し、詠唱中断時はその実行IDに属する候補を片づける。[主標的の関係](../../../Assets/MyAsset/Core/AI/Core/AITargetRelations.cs)・[実行イベント](../../../Assets/MyAsset/Runtime/Combat/GameActionRuntime.cs)・[散弾の生成](../../../Assets/MyAsset/Runtime/Combat/Projectile/ProjectileManager.cs)

発射直前にBだけを遮蔽物の裏へ移す。発射数は二、各弾の `TargetRef` はAとC、AIの主標的は元の個体のまま。Bを同じオブジェクトから再出現させても新世代へ古い印を渡さない。全員が失効した場合と詠唱を中断した場合は発射数ゼロ。敵と味方の配置を逆にしても同じ条件で通す。地面指定の爆発については着弾時に被弾者を求める。[弾の対象と貸出世代](../../../Assets/MyAsset/Core/Combat/Projectile/Projectile.cs)

### 3. 一発の明示ロックと、床の予告を描き分ける

騎士とシスターを並べ、上下に床がある橋で同じ方向から撃つ。方向表示には各弾の明示ロック、飛来方向、騎士のカメラを渡す。弾の `LeaseId` と標的の世代を照合し、解除・消滅・通過時にその弾の弧を消す。範囲予告には予備動作中から、その攻撃が当たる床・形状を渡し、中断時に消す。現在の `ProcessExplosion` は着弾時のXY平面の判定なので、3Dの橋では上下の床を別々に置いて予告面と命中面を照合する。[追尾更新と爆発](../../../Assets/MyAsset/Runtime/Combat/Projectile/ProjectileManager.cs)・[HUDの更新先](../../../Assets/MyAsset/Runtime/UI/HudController.cs)

| 同じ配置で変える条件 | 画面と音の合格条件 |
|---|---|
| 術師はシスターを主標的にし、三発のうち一発を騎士にロック | 騎士宛ての一発だけに方向の弧。カメラを回すと飛来側が追従する |
| シスター宛ての爆発弾が騎士の近くを通る | 橋上の着弾範囲だけを事前に赤く示す。橋の下は元の表示 |
| 同じ爆発弾を騎士にロック／無照準の通常弾に変更 | 前者は方向の弧と床予告の両方。後者は両方とも出ない |
| 通常の近接／危険指定の近接 | 構えと振りで通常攻撃を読める。危険指定だけ予備動作に警告音が付き、画面端には弧を出さない |

危険な近接の音はガード設計にある。該当する攻撃の予備動作から実音が鳴ることを試作で確かめる。[ガード警告の設計](../../../designs/systems/ParryGuardSystem.md#ジャストガード不可攻撃の警告)

## 機能DBへ戻す条件

対象は `DungeonExpedition_AllyRoster` の `select-player-eight-way-multilock`、`assign-ai-cast-projectile-targets`、`show-lock-and-area-warnings`。試作を通した経路から、マニフェストの `steps` を実際に実行した順に、`tests` を使用したfixtureに更新する。純粋な方向選択のテストだけを通した場合はその範囲を記す。実入力から各弾の生成、または予告の表示・消去まで該当経路をPlayModeで確認した段階で、その行を `runtime_tested` にする。各経路に対応するstepとテストファイルのSHA-256、実行件数を残す。警告表示の経路には橋の上下での目視・警告音の試聴結果も記録する。親機能を登録済みのDBに対象行を取り込み、次の照会で状態と陳腐化したハッシュを確認する。[マニフェスト](../../../designs/call-paths.json)・[DB入力の検証](../../../tools/feature-db.py)

```sh
python tools/feature-db.py call-paths DungeonExpedition_AllyRoster
```
