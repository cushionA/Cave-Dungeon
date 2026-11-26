# System: SaveSystem
Section: 1 — MVP

## 責務
セーブポイントでのデータ永続化、回復、ファストトラベル。ISaveableインタフェースにより各システムが独立してシリアライズ/デシリアライズする。

## 依存
- 入力: ISaveable実装システム群、MapSystem（ファストトラベル先）
- 出力: セーブデータファイル、回復処理、ファストトラベル実行

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| SaveManager | セーブ/ロード統括 | No (GameManager保持) |
| SavePoint | セーブポイントオブジェクト | Yes (IInteractable) |
| SaveDataStore | セーブデータの構造・シリアライズ | No |

## スロットの読み込み

登録済みの保存項目を復元できた時点で、アクティブスロットとロード世代を確定する。物語データの形式や復元に必要なQuest Machineの準備状態は、ゲーム内の状態を変更する前に確認する。途中で例外が発生した場合は、変更を試みた項目を直前の保存可能な状態へ戻す。`Loaded` は確定後に購読先ごとに通知し、例外はログに記録する。通知中は保存・ロード・スロット変更を拒否する。通知を受けてこれらの操作が必要な場合は、通知終了後に実行する。

古いセーブや空のスロットに `FlagManager` の項目がなければ、グローバル・マップローカルの両フラグを初期化する。これにより別スロットで進めた物語や探索のフラグが混ざらない。保存項目がある場合はフラグの形式を事前検証し、不正なら他の項目を復元する前にロードを中止する。保存時のクリア記録は対象スロットに対応するものを使う。[セーブ管理](../../Assets/MyAsset/Core/Save/SaveManager.cs)・[フラグ保存](../../Assets/MyAsset/Core/Save/FlagManager.cs)

周回番号の保存項目がない旧セーブは初周として復元する。保存項目がある場合は、1以上かつ `int` の範囲内の整数を受け付ける。値が不正な場合は他の保存項目を復元する前にロードを中止し、現在の周回とスロットを保持する。[周回番号の復元](../../Assets/MyAsset/Core/Save/ExpeditionPlaythrough.cs)

## 次の周回を始める

指定したクリアイベントを `ExpeditionNextPlaythroughPanel.MarkCampaignCleared` へつなぐ。遭遇を指定する場合は `Completed` でクリアを記録する。パネルで「次の周回へ」を選んで確認すると、次周回の状態を適用し、現在のスロットへディスク保存できた時点で確定してシーンを切り替える。適用または書き込みに失敗した場合は直前の周回とクリア状態に戻る。確認後にスロットやロード状態が変わった場合は、もう一度選び直す。[パネルの実装](../../Assets/MyAsset/Runtime/UI/ExpeditionNextPlaythroughPanel.cs)

試作では、解放済みの召喚種、通常編成、デッキを引き継ぐ。Quest Machine の進捗と Dialogue System の変数は、同じ依頼や会話を次周でやり直せるよう両方とも初期化する。周回をまたぐ物語の条件は、作者が `carryGlobalFlags` に指定したゲーム側のグローバルフラグに記録する。マップローカルフラグも初期化する。周回ごとに初期化する保存項目は `IResetOnNextPlaythrough` を実装する。[保存処理](../../Assets/MyAsset/Core/Save/ExpeditionNextPlaythrough.cs)・[物語データの連携](../../Assets/Integration/PixelCrushersSaveSlotBridge.cs)

「坑道の番人」の試作シーンは、撃破後に同じ部屋を次の周回の開始地点として開き直す。本編ではエンディングのクリア通知と開始シーンを指定する。[試作シーン](../../Assets/Scenes/BossArena3DTestScene.unity)

## セーブポイント機能

遠征の試作シーンでは入口の `SAVE` に近づき、操作ボタン（キーボードの E、ゲームパッドの Y）を長押しして保存する。現在のスロットへの書き込みが成功すると、騎士のHP・MP・スタミナが全回復し、目印が `SAVED` に変わる。保存には通常の遠征編成、物語の進捗、現在の周回と再開地点が含まれる。再開時は同じシーンの同じ `Save Point Id` に対応する `Resume Anchor` に騎士を配置する。[3Dセーブポイント](../../Assets/MyAsset/Runtime/World/ExpeditionSavePoint3D.cs) [試作シーン](../../Assets/Scenes/ExpeditionMock3DScene.unity)

指定区画への到達や指定遭遇の勝利では、同じスロットに現在のHP・MP・スタミナ・アーマーをオートセーブする。戦闘で消耗していれば「つづきから」でもその残量を現在の上限内で引き継ぐ。再開地点には消耗した騎士が安全に行動を始められる場所を指定する。入口で手動保存する場合は、休息による回復後の値を保存する。[オートセーブ](../../Assets/MyAsset/Runtime/World/ExpeditionAutosave3D.cs)

```
セーブポイント使用時:
1. HP/MP/スタミナ全回復
2. セーブデータ書き出し
3. エリア内の敵リスポーン
4. ファストトラベルメニュー表示（開通済みポイント一覧）
```

## セーブデータ構造
```csharp
[Serializable]
public class SaveDataStore
{
    public string currentSavePointId;
    public string currentAreaId;
    public int playerLevel;
    public int[] statAllocation;        // STR, DEX, INT, VIT, MND, END
    public int currency;
    public float playTime;
    public Dictionary<string, SaveData> systemData; // ISaveable.SaveIdをキーに各システムのデータ

    // ISaveable実装例:
    // - EquipmentSystem → 装備中のアイテムID
    // - InventorySystem → 所持アイテムリスト
    // - MapDataManager → 訪問済みエリア・探索率
    // - FlagManager → フラグ状態（Section2以降）
}
```

## ファストトラベル

3D遠征では訪ねた転移地点を、安定した地点IDで同じセーブスロットのフラグに記録する。保存地点ではチェックポイントと訪問フラグを同じ書き込みで確定する。区画が未ロードなら先に加算ロードし、着地点の床と空きを確認してから、登録世代を保った同じ騎士を配置する。中止した場合はその転移のためにロードした区画を解放する。通常の移動がその区画を要求した場合は、そのまま通常の遷移に利用する。移動した時点の持ち物と能力値を引き継ぐ。[3Dファストトラベル](../../Assets/MyAsset/Runtime/World/ExpeditionFastTravel3D.cs)

```
1. SavePoint使用 → ファストトラベルメニュー表示
2. 開通済みSavePointを一覧表示
3. 選択 → LevelStreamingManager経由でエリア遷移
4. 遷移先SavePointの位置にプレイヤー配置
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| セーブデータ書出 | JSON or Binary でファイル保存 | EditMode | High |
| セーブデータ読込 | ファイルからの復元 | EditMode | High |
| HP/MP/スタミナ回復 | セーブポイント使用時の全回復 | EditMode | High |
| ファストトラベル | 開通済みポイント間の移動 | PlayMode | Medium |
| 敵リスポーン | セーブポイント使用時の敵再配置 | PlayMode | Medium |
| セーブスロット管理 | 複数セーブスロット | EditMode | Low |

## 設計メモ
- セーブ形式: JSON（開発中）→ バイナリ（リリース時）
- セーブデータのバージョニング対応（フィールド追加時の後方互換）
- 暗号化はリリース時に追加（開発中はプレーンテキスト）
