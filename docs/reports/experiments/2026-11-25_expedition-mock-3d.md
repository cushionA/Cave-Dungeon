# 3D遠征モックの確認範囲

Unity で `ExpeditionMock3DScene` を開いて Play する。坑道の依頼と工兵の編成、シスターへの連携と戦術編集、区画を読み込む通路、ボスと次周を一つのマップに配置した。集団戦は橋の専用試作でも確認できる。[遠征モック](../../../Assets/Scenes/ExpeditionMock3DScene.unity)・[橋の集団戦](../../../Assets/Scenes/GroupBattle3DTestScene.unity)

1. 工兵長の近くで E／パッド上ボタンを長押しして依頼を受ける。北の測量地点に入り、戻って報告する。シスターとの会話で編成を開き、工兵を追加して確定すると出撃を選べる。編成画面でデッキの保存・読込も試せる。
2. シスターの近くで V／十字キー上を押すと防御支援を受ける。坑道の `TACTICS` 台で E／パッド上ボタンを長押しすると、シスターの行動と発動条件を編集できる。
3. 北の `SENTINEL PASSAGE` を奥へ進むと隣接区画を読み込む。騎士の左クリック／パッド左ボタンは装備中の武器で攻撃、Q／右肩ボタンは選択中の魔法枠を使用、右クリック／左肩ボタンはガード。
4. `CAVE SENTINEL` を倒し、次周を選んで確定すると開始地点へ戻る。モックのセーブ用フォルダーは `ExpeditionMock3D`。この試作では坑道の依頼とボス戦を任意の順序で進められる。[操作設定](../../../Assets/InputSystem_Actions.inputactions)・[周回と保存の設定](../../../designs/systems/SaveSystem.md)

`MEMORIAL` で E／パッド上ボタンを長押しすると専用イベントが始まる。再生終了または「スキップ」で、同じ騎士とカメラで探索に復帰する。再生済みの状態は同じセーブスロットで復元する。[イベントシーン](../../../Assets/Scenes/ExpeditionEvent3DScene.unity)

Tab／LT または `GEAR & GROWTH` 台で「戦闘準備」を開く。武器は騎士の剣と坑道の弩、コアは探索と残り火、魔法は追尾弾と火輪から選べる。魔法は使いたい枠を選んでからセットする。`SURVEY` の初回通過で100経験値、`CAVE SENTINEL` の撃破で150経験値を獲得する。レベルが上がったら「能力値」で残りポイントを配分できる。[装備と魔法枠](../../../designs/systems/WeaponSystem.md)・[成長](../../../designs/systems/LevelUpSystem.md)

戦闘準備の「連携」では、所持している防御支援と攻撃支援から1つを装備する。シスターの近くで V／十字キー上を押すと装備中の支援が騎士にかかる。選択はセーブスロットごとに保存し、支援ごとのクールタイムは装備を切り替えても進み続ける。[3D連携の仕様](../../../designs/systems/CoopAction.md)・[装備画面の試験](../../../Assets/Tests/PlayMode/ExpeditionMock3DScenePlayTests.cs)

開始地点の近くに回復薬（`HEALING DRAUGHT`）3個と火炎瓶（`EMBER FLASK`）2個を配置した。E／パッド上ボタンを長押しして拾い、「消耗品」ボタンまたは Esc／Start で登録画面を開く。所持品を4枠のベルトに登録し、1・2／十字キー左右で切り替え、R／パッド右ボタンで使用する。回復薬はHPを最大25回復し、火炎瓶は周囲に火属性の攻撃判定を出す。火炎瓶の基礎威力は `22 + 知力 × 0.65` を四捨五入した値で、命中時は属性防御を適用する。[所持品とベルトの仕様](../../../designs/systems/InventorySystem.md)

工兵は確定編成から所定の地点に出現し、通常探索では騎士について歩く。凍結や移動を占有する行動が終わると追従を再開する。区画の召喚設定で `Follow Waypoints` に床上の中継点を指定すると、工兵は壁を迂回できる。床のレイヤーを `Follow Ground Layers` に、通行を妨げる地形のレイヤーを `Follow Obstruction Layers` に指定する。迂回経路を組めない場合も、次の区間に足場と通行幅があれば直進し、塞がっていれば待つ。中継点が空なら従来の直進で同行する。[召喚の出撃と同行](../../../Assets/MyAsset/Runtime/AI/ExpeditionSummonDeployment3D.cs)・[迂回の試験](../../../Assets/Tests/PlayMode/ExpeditionSummonDeployment3DPlayTests.cs)

3Dの爆発物は、`ExplosiveProp3D` と同じオブジェクトの実体コライダーを `Hit Collider` に指定し、攻撃プロファイルの `Target Layers` にそのレイヤーを含める。近接や直撃弾が指定形状に届くと一度だけ起爆する。同距離の有効な候補が複数ある場合は、シーンとHierarchyの順を使う。手前の壁や別の爆発物を `Obstruction Layers` に含めると、奥にある爆発物への直接攻撃を遮る。起爆後の爆風は、壁をはさんでも同じ物理シーンの球内にいる敵へ火属性ダメージを与える。演出は `On Detonated` に設定できる。[爆発物の実装](../../../Assets/MyAsset/Runtime/Combat/ExplosiveProp3D.cs)・[実際の攻撃による起爆試験](../../../Assets/Tests/PlayMode/SpatialCombat3DPlayTests.ExplosiveProps.cs)

工兵を解放したら、坑道入口脇の `REINFORCEMENT DRILL` で E／パッド上ボタンを長押しし、増援訓練を始められる。編成画面の「増援」に切り替え、プリセットを読み込んで確定すると、訓練用の追加枠に工兵1体が出撃する。`DRILL FINISH` に到達すると成功し、`WITHDRAW` を操作すると撤退する。どちらでも増援を解除し、通常編成は探索に同行し続ける。[訓練の操作](../../../Assets/MyAsset/Runtime/World/ExpeditionReinforcementDrill3D.cs)・[増援の試験](../../../Assets/Tests/PlayMode/ExpeditionMock3DScenePlayTests.cs)

入口の `SAVE` と、`SENTINEL PASSAGE` の先にある坑道奥の転移地点は、E／パッド上ボタンの長押しで操作する。各地点を訪問すると同じスロットの移動先に登録される。入口では通常保存と同時に訪問記録を確定し、坑道奥の訪問記録は次の保存に含める。移動先リストから選ぶと、必要な区画を読み込んで同じ騎士が到着地点へ移る。[ファストトラベル](../../../Assets/MyAsset/Runtime/World/ExpeditionFastTravel3D.cs)・[転移の試験](../../../Assets/Tests/PlayMode/ExpeditionFastTravel3DPlayTests.cs)

通常探索で工兵を出撃させたら、F／十字キー下を短く押して号令を出す。長押しして離すたびに「集合」「集中攻撃」「ここを守れ」「おまかせ」を切り替える。「集中攻撃」は敵を、「ここを守れ」は床を照準する。集団戦が始まるとその遭遇が号令を受け付け、終了後は通常の同行へ戻る。[探索号令の試験](../../../Assets/Tests/PlayMode/ExpeditionSummonDeployment3DPlayTests.FieldOrders.cs)

3D遭遇の `Explosive Props` に登録した爆発物は、敵の近くで味方が爆風に入らず、単発直進弾の最初の接触がその爆発物に届く場合にAIが狙う。発射の直前にも条件を確認する。弾の太さによる遮蔽、遭遇外の味方、詠唱中の移動は専用PlayModeで検証する。配置した爆発物は区画を再読み込みすると復活する。[AIの起爆判断](../../../Assets/MyAsset/Runtime/AI/EncounterExplosiveAim3D.cs)・[実際の起爆試験](../../../Assets/Tests/PlayMode/EncounterExplosiveAim3DPlayTests.cs)

## 操作と機能の境界

| 項目 | 現在の接続・検証 | プレイヤー向けに残る作業 |
|---|---|---|
| シスターとの連携・AI調整 | `Cooperation` は V／十字キー上。戦闘準備で選んだ支援を騎士に発動し、クールタイム明けの無料分と必要MPを共有実行で扱う。シスターは通常時に追従し、指定集団戦では戦闘側が移動を担当する。3D用の戦術編集では確認した設定を登録世代が一致するシスターに適用する。[連携入力](../../../Assets/Tests/PlayMode/PlayerCooperationInput3DPlayTests.cs)・[戦術編集](../../../Assets/Tests/PlayMode/CompanionAISettings3DPlayTests.cs) | 3Dのワープと盾による反射・足場、対応行動の追加。 |
| 専用イベントシーン | 加算ロードした専用シーンのカメラとTimelineを使い、再生終了とスキップから元の騎士へ操作を戻す。鑑賞済みフラグも保存できる。NPC会話・依頼は従来のDialogue SystemとQuest Machineに接続している。[イベント試験](../../../Assets/Tests/PlayMode/ExpeditionEvent3DScenePlayTests.cs)・[会話・依頼の保存](../../../Assets/Tests/PlayMode/PixelCrushersStorySlotPlayTests.cs) | 本編のイベント台本、カメラ、音声、発生条件の設定。 |
| エリアのストリーミング | 騎士の3Dコントローラーと進入方向を境界で判定し、隣接シーンを加算ロードする。隣接エリア間の実シーンを移動して、騎士が継続することを確かめる専用試験がある。[境界実装](../../../Assets/MyAsset/Runtime/Streaming/AreaBoundaryTrigger3D.cs)・[隣接エリア試験](../../../Assets/Tests/PlayMode/LevelStreaming3DPlayTests.cs) | 本編マップの区画・境界の配置と、複数の遷移や復元を通した検証。 |
| 回復・攻撃アイテム | 配置した拾得物から所持品へ追加し、ベルトで登録・選択・使用する。モーションの効果発揮で1個だけ消費し、回復または攻撃判定と粒子演出を出す。効果発揮前に中断すると所持数を維持する。所持数と登録はスロットごとに保存する。[モックの拾得と使用](../../../Assets/Tests/PlayMode/ExpeditionConsumablesMockScenePlayTests.cs)・[攻撃と中断](../../../Assets/Tests/PlayMode/PlayerItemUse3DPlayTests.cs)・[在庫と保存](../../../Assets/Tests/EditMode/InventoryUsageBeltTests.cs) | 本編の品ぞろえと配置。モックの配置物はシーンを読み直すと復活する。 |
| 武器・遠距離・魔法スロット・コア・成長 | 所持品から剣・弩とコアを交換し、魔法を3枠に登録・選択する。入力は選択した攻撃と魔法へつながり、発射済みの弾は発射時の値を使う。探索・撃破の経験値で得たポイントを配分すると騎士の能力が更新される。[戦闘準備の試験](../../../Assets/Tests/PlayMode/ExpeditionLoadoutPanel3DPlayTests.cs)・[実入力の攻撃と魔法](../../../Assets/Tests/PlayMode/PlayerCombatLoadout3DPlayTests.cs)・[成長試験](../../../Assets/Tests/PlayMode/PlayerProgression3DPlayTests.cs) | 本編での武器・コア・魔法の入手先、敵と探索地点の報酬量の設定。 |
| 追尾・命中・索敵 | 明示ロックに従う3D弾、遮蔽を含む命中、登録世代ごとの対象照合、同時マルチロックを個別に検証する。陣営の観測は視界と遮蔽を参照する。[3D戦闘試験](../../../Assets/Tests/PlayMode/SpatialCombat3DPlayTests.cs)・[陣営観測](../../../Assets/Tests/PlayMode/EncounterObservation3DPlayTests.cs) | 本編の敵・技への索敵距離と予告設定。3D飛翔中に対象を失った弾は自動で別の敵を選び直さない。 |
| 高低差のある地形でのAI | 橋の上下を分けた命中、目標の到達、前衛・控えの配置を実行検証済み。遮蔽が開いた敵が斜面と段差を登り、上段で近接を当てる専用試験も追加した。[橋の集団戦](../../../Assets/Tests/PlayMode/GroupBattle3DScenePlayTests.cs)・[斜面のAI試験](../../../Assets/Tests/PlayMode/EncounterCombat3DPlayTests.cs) | 複雑な迂回、橋の譲り合い、多数の地形での索敵と行動順の調整。 |
| 状態異常・装備パッシブ | 技と装備の命中時効果は別々に蓄積する。装備の能力加算と一時的な `AttackUp` は別経路で、一時効果解除後も装備加算を維持する。実際の3D攻撃・装備・解除APIで照合する試験を追加した。[3D試験](../../../Assets/Tests/PlayMode/SpatialCombat3DPlayTests.StatusEffects.cs)・[共通処理の試験](../../../Assets/Tests/EditMode/EquipmentCombatFlowTests.cs) | テスト用効果の本編アイテム・技への設定と、プレイヤーが使う解除アクションの接続。 |

## Linux Playerでの手操作

2026-11-26にキーボードとマウスで、新規スロットの開始、測量の受注と報告、工兵の編成と召喚、区画の歩行通過、坑道入口と坑道奥の転移の往復、番人の撃破、第2周開始を確認した。第2周では石碑の専用カメラで封印が開く動きと、再生終了後に同じ位置へ戻ることを確認した。探索で集合とおまかせを操作し、移動後のシスターと工兵の追従再開を確認した。[遠征モック](../../../Assets/Scenes/ExpeditionMock3DScene.unity)・[探索号令の試験](../../../Assets/Tests/PlayMode/ExpeditionSummonDeployment3DPlayTests.FieldOrders.cs)

2026-11-27に旧セーブを開き、「戦闘準備」の連携タブで攻撃支援を選択して保存した。Linux Playerを起動し直して同じスロットを開き、攻撃支援が選択済みとして表示されることを確認した。[連携の仕様](../../../designs/systems/CoopAction.md)

番人の攻撃前に、赤い円が足元の床に表示された。騎士は弩の構えから発射へ移り、武器の動きと弾を確認できた。警告確認用のシーンでは、通常近接の録音が無音で、危険近接の予備動作中だけ警告音の波形が記録された。橋上では赤い床予告、発射後の画面端の方向警告、両方の消去を確認した。橋の下では足元に同じ円は出なかった。[警告確認用シーン](../../../Assets/Scenes/CharacterMotor3DTestScene.unity)

区画の継ぎ目では床面が重なって縞状にちらついた。番人は撃破後も紫の仮モデルが画面に残り、撃破は「次の周回へ」の表示で判別した。探索号令は選択中の号令名が画面に出ないため、切り替え結果は味方の動きで判別した。モーションの確認には試作用の仮モデルを使用した。[遠征モック](../../../Assets/Scenes/ExpeditionMock3DScene.unity)

## 自動検証

2026-11-27の全体回帰では、Unity 6000.6.8f1 で EditMode 2,390件と PlayMode 538件がすべて通過した。転移で先読み区画に到着すると対象スロットへのオートセーブが一度だけ実行され、到着が成立しなければ保存の通知は発生しないことも確認した。転移では未ロードの区画の復帰、中断後の区画解放と再選択、通常遷移への引き継ぎ、壁との干渉、35度の斜面への到着を検証した。Linux Playerでは旧形式のセーブから入口を再開し、新しい地点IDで訪問記録が保存されることを確認した。PlayModeでは書き込み失敗時に訪問が登録されないことを確認した。[転移の試験](../../../Assets/Tests/PlayMode/ExpeditionFastTravel3DPlayTests.cs)・[保存失敗の試験](../../../Assets/Tests/PlayMode/ExpeditionSavePointMockPlayTests.cs)

装備と魔法枠はディスク往復と旧セーブの読み込みを確認した。PlayModeでは石碑の長押しから専用イベントが始まり、通常終了とスキップのどちらでも元の騎士でガードできることを確認した。実シーンの装備台では武器とコアを交換して魔法枠を登録し、測量で得たポイントを配分した。番人の150経験値も実戦で確認した。[装備の保存](../../../Assets/Tests/EditMode/PlayerCombatLoadoutSelectionTests.cs)・[イベントの実入力](../../../Assets/Tests/PlayMode/ExpeditionEvent3DScenePlayTests.cs)・[統合モック試験](../../../Assets/Tests/PlayMode/ExpeditionMock3DScenePlayTests.cs)

統合モックの試験では、配置したシスターが騎士に防御支援をかけて追従し、騎士が二区画を通過すると後方区画が解除されることを確認した。二人の登録世代は通過前後で同じだった。騎士に設定した剣で番人を倒すと遭遇が完了し、「次の周回へ」ボタンが表示された。依頼と編成・出撃、物語の保存、次周の確定・シーン遷移・復元はそれぞれの専用試験で確認した。[統合モック試験](../../../Assets/Tests/PlayMode/ExpeditionMock3DScenePlayTests.cs)・[依頼のシーン試験](../../../Assets/Tests/PlayMode/ExpeditionQuest3DScenePlayTests.cs)・[物語の保存](../../../Assets/Tests/PlayMode/PixelCrushersStorySlotPlayTests.cs)・[次周開始](../../../Assets/Tests/PlayMode/ExpeditionNextPlaythroughScenePlayTests.cs)

消耗品の試験ではモックに配置した両方の品を拾い、回復薬で手元のモーションと回復を確認した。火炎瓶は同じ床の範囲内の敵に命中し、両方の品で専用のパーティクルが発生した。効果発揮前の中断・ロード・個体の再登録では消費されず、所持品とベルトは同じセーブスロットで復元できることを専用試験で確認した。[モックの消耗品試験](../../../Assets/Tests/PlayMode/ExpeditionConsumablesMockScenePlayTests.cs)・[使用と中断](../../../Assets/Tests/PlayMode/PlayerItemUse3DPlayTests.cs)・[ベルトの操作](../../../Assets/Tests/PlayMode/ConsumableBeltPanel3DPlayTests.cs)

依頼の専用シーンでは、会話から出撃した工兵が、歩いて移動した騎士に追いつき、同じ登録世代とシーンを保った。通常編成は凍結解除後と集団戦の移動担当が外れた後に追従を再開し、戦闘専用の増援は通常の追従に加わらないことを個別に確認した。[依頼からの出撃試験](../../../Assets/Tests/PlayMode/ExpeditionQuest3DScenePlayTests.cs)・[同行と移動の切替試験](../../../Assets/Tests/PlayMode/ExpeditionSummonDeployment3DPlayTests.cs)
