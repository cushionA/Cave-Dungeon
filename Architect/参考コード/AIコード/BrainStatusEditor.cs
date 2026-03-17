#if UNITY_EDITOR
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using static CharacterController.AIManager;
using static CharacterController.StatusData.BrainStatus.TriggerJudgeData;

namespace CharacterController.StatusData
{
    /// <summary>
    /// BrainStatusのエディタ専用拡張クラス
    /// このクラスはUnity Editorでのみ動作し、実行時には完全に除外される
    /// AIキャラクターの行動判定データを視覚的に編集するためのUI拡張機能を提供
    /// </summary>
    public partial class BrainStatus
    {
        #region エディタ専用プロパティ - TriggerJudgeData

        /// <summary>
        /// AIの行動トリガー判定データ構造体のエディタ拡張
        /// 条件に応じて異なる型のデータを適切なUIで表示する
        /// </summary>
        public partial struct TriggerJudgeData
        {
            /// <summary>
            /// 下限値をBitableBool（論理値）として扱うエディタプロパティ
            /// OR判定/AND判定の選択に使用される
            /// ShowIfアトリビュートにより、特定条件下でのみ表示
            /// </summary>
            [ShowInInspector]
            [ShowIf("@IsLowerValueBittableBool()")]  // 条件判定メソッドで表示制御
            [LabelText("@GetLowerValueLabel()")]     // 動的ラベル生成
            private BitableBool LowerValueAsBitableBool
            {
                get => (BitableBool)judgeLowerValue;  // int値をBitableBoolにキャスト
                set => judgeLowerValue = (int)value;  // BitableBoolをint値に変換して保存
            }

            /// <summary>
            /// 下限値をCharacterBelong（キャラクター所属）として扱うエディタプロパティ
            /// 特定陣営の判定条件で使用される
            /// </summary>
            [ShowInInspector]
            [ShowIf("@IsLowerValueCharacterBelong()")]
            [LabelText("@GetLowerValueLabel()")]
            private CharacterBelong LowerValueAsCharacterBelong
            {
                get => (CharacterBelong)judgeLowerValue;
                set => judgeLowerValue = (int)value;
            }

            /// <summary>
            /// 上限値をRecognizeObjectType（認識オブジェクト種別）として扱うエディタプロパティ
            /// 周囲のオブジェクト・地形判定で使用される
            /// EnumToggleButtonsでビットフラグを視覚的に選択可能
            /// </summary>
            [ShowInInspector]
            [ShowIf("@IsUpperValueRecognizeObject()")]
            [LabelText("@GetUpperValueLabel()")]
            [EnumToggleButtons]  // 複数選択可能なトグルボタンUI
            private RecognizeObjectType UpperValueAsRecognizeObject
            {
                get => (RecognizeObjectType)judgeUpperValue;
                set => judgeUpperValue = (int)value;
            }

            /// <summary>
            /// 上限値をBrainEventFlagType（脳イベントフラグ種別）として扱うエディタプロパティ
            /// 特定イベント発生判定で使用される
            /// </summary>
            [ShowInInspector]
            [ShowIf("@IsUpperValueBrainEvent()")]
            [LabelText("@GetUpperValueLabel()")]
            [EnumToggleButtons]
            private BrainEventFlagType UpperValueAsBrainEvent
            {
                get => (BrainEventFlagType)judgeUpperValue;
                set => judgeUpperValue = (int)value;
            }

            /// <summary>
            /// 個別行動選択用のドロップダウンプロパティ
            /// actData配列から実行する行動をインデックスで選択
            /// ValueDropdownで動的にリストを生成し、視覚的に選択可能
            /// </summary>
            [ShowInInspector]
            [ShowIf("@IsIndividualActionMode()")]  // 個別行動モード時のみ表示
            [LabelText("実行する行動")]
            [ValueDropdown("@GetActionDropdownList()")]  // 動的ドロップダウンリスト生成
            [InfoBox("攻撃データ配列から行動を選択してください。配列が空の場合は先に攻撃データを設定してください。",
                     InfoMessageType.Info, "@IsActionDataEmpty()")]  // 警告メッセージ表示条件
            private byte TriggerNumAsActionIndex
            {
                get => triggerNum;
                set => triggerNum = value;
            }

            #region エディタ専用メソッド

            /// <summary>
            /// 判定条件に基づいて説明文を生成するメソッド
            /// Inspectorでの理解を助けるための詳細説明を提供
            /// </summary>
            /// <returns>条件に対応した説明文</returns>
            private string GetConditionDescription()
            {
                switch ( judgeCondition )
                {
                    case ActTriggerCondition.特定の対象が一定数いる時:
                        return "フィルターに該当する対象が指定範囲の数だけ存在する時に条件成立";
                    case ActTriggerCondition.HPが一定割合の対象がいる時:
                        return "HPが指定範囲の割合（0-100%）の対象が存在する時に条件成立";
                    case ActTriggerCondition.MPが一定割合の対象がいる時:
                        return "MPが指定範囲の割合（0-100%）の対象が存在する時に条件成立";
                    case ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以上密集している時:
                        return "対象の周囲に指定陣営が指定人数以上密集している時に条件成立";
                    case ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以下しかいない時:
                        return "対象の周囲に指定陣営が指定人数以下しかいない時に条件成立";
                    case ActTriggerCondition.周囲に指定のオブジェクトや地形がある時:
                        return "指定したオブジェクトタイプが周囲に存在する時に条件成立";
                    case ActTriggerCondition.対象が一定数の敵に狙われている時:
                        return "指定範囲の数の敵から狙われている時に条件成立";
                    case ActTriggerCondition.対象のキャラの一定距離以内に飛び道具がある時:
                        return "飛び道具が指定範囲の距離内に存在する時に条件成立";
                    case ActTriggerCondition.特定のイベントが発生した時:
                        return "指定したイベントが発生した時に条件成立";
                    default:
                        return "条件なし";
                }
            }

            /// <summary>
            /// 下限値フィールドの動的ラベル生成メソッド
            /// 選択された条件に応じて適切なラベルテキストを返す
            /// </summary>
            /// <returns>条件に応じたラベル文字列</returns>
            private string GetLowerValueLabel()
            {
                switch ( judgeCondition )
                {
                    // OR/AND判定を選択する条件
                    case ActTriggerCondition.周囲に指定のオブジェクトや地形がある時:
                    case ActTriggerCondition.特定のイベントが発生した時:
                        return "判定方法（FALSE:OR判定, TRUE:AND判定）";
                    // 陣営を選択する条件
                    case ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以上密集している時:
                    case ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以下しかいない時:
                        return "対象陣営";
                    // 数値範囲の下限を設定する条件
                    default:
                        return "下限値";
                }
            }

            /// <summary>
            /// 上限値フィールドの動的ラベル生成メソッド
            /// 選択された条件に応じて適切なラベルテキストを返す
            /// </summary>
            /// <returns>条件に応じたラベル文字列</returns>
            private string GetUpperValueLabel()
            {
                switch ( judgeCondition )
                {
                    // オブジェクトタイプを複数選択する条件
                    case ActTriggerCondition.周囲に指定のオブジェクトや地形がある時:
                        return "オブジェクトタイプ（複数選択可）";
                    // イベントタイプを複数選択する条件
                    case ActTriggerCondition.特定のイベントが発生した時:
                        return "イベントタイプ（複数選択可）";
                    // 人数を設定する条件
                    case ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以上密集している時:
                    case ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以下しかいない時:
                        return "必要人数";
                    // 数値範囲の上限を設定する条件
                    default:
                        return "上限値";
                }
            }

            // UI表示条件判定メソッド群
            private bool ShowLowerValue() => judgeCondition != ActTriggerCondition.条件なし;
            private bool ShowUpperValue() => judgeCondition != ActTriggerCondition.条件なし;
            private bool IsLowerValueEnum() => IsLowerValueBittableBool() || IsLowerValueCharacterBelong();
            private bool IsUpperValueEnum() => IsUpperValueRecognizeObject() || IsUpperValueBrainEvent();

            /// <summary>
            /// 下限値をBitableBoolとして扱うべき条件かを判定
            /// </summary>
            private bool IsLowerValueBittableBool()
            {
                return judgeCondition == ActTriggerCondition.周囲に指定のオブジェクトや地形がある時 ||
                       judgeCondition == ActTriggerCondition.特定のイベントが発生した時;
            }

            /// <summary>
            /// 下限値をCharacterBelongとして扱うべき条件かを判定
            /// </summary>
            private bool IsLowerValueCharacterBelong()
            {
                return judgeCondition == ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以上密集している時 ||
                       judgeCondition == ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以下しかいない時;
            }

            /// <summary>
            /// 上限値をRecognizeObjectTypeとして扱うべき条件かを判定
            /// </summary>
            private bool IsUpperValueRecognizeObject()
            {
                return judgeCondition == ActTriggerCondition.周囲に指定のオブジェクトや地形がある時;
            }

            /// <summary>
            /// 上限値をBrainEventFlagTypeとして扱うべき条件かを判定
            /// </summary>
            private bool IsUpperValueBrainEvent()
            {
                return judgeCondition == ActTriggerCondition.特定のイベントが発生した時;
            }

            // 行動選択ドロップダウン関連のメソッド群

            /// <summary>
            /// 個別行動モードかどうかを判定
            /// 個別行動モード時は攻撃データ配列からの行動選択UIを表示
            /// </summary>
            private bool IsIndividualActionMode() => triggerEventType == TriggerEventType.個別行動;

            /// <summary>
            /// 通常のトリガー番号フィールドを表示するかを判定
            /// 個別行動モード以外では通常の数値入力フィールドを表示
            /// </summary>
            private bool ShowNormalTriggerNum() => !IsIndividualActionMode();

            /// <summary>
            /// 攻撃データ配列が空かどうかを判定
            /// 空の場合は警告メッセージを表示
            /// </summary>
            private bool IsActionDataEmpty() => GetCurrentBrainStatusActionCount() == 0;

            /// <summary>
            /// 行動選択用のドロップダウンリストを動的生成
            /// BrainStatusのactData配列から行動名とインデックスのペアを作成
            /// </summary>
            /// <returns>ValueDropdownで使用するリスト</returns>
            private ValueDropdownList<byte> GetActionDropdownList()
            {
                var list = new ValueDropdownList<byte>();
                var brainStatus = GetCurrentEditingBrainStatus();

                // BrainStatusと行動データが存在する場合
                if ( brainStatus != null && brainStatus.actData != null )
                {
                    // 各攻撃データに対してドロップダウン項目を作成
                    for ( int i = 0; i < brainStatus.actData.Length; i++ )
                    {
                        var actionData = brainStatus.actData[i];
                        // アクション名が設定されていない場合はデフォルト名を生成
                        string displayName = string.IsNullOrEmpty(actionData.actionName)
                            ? GenerateDefaultActionName(actionData, i)
                            : actionData.actionName;
                        list.Add(displayName, (byte)i);
                    }
                }

                // データが存在しない場合のフォールバック
                if ( list.Count == 0 )
                {
                    list.Add("行動データなし", 0);
                }

                return list;
            }

            /// <summary>
            /// 現在編集中のBrainStatusインスタンスを取得
            /// 複数の方法でBrainStatusの参照を試行し、最も適切なものを返す
            /// </summary>
            /// <returns>現在編集中のBrainStatusインスタンス、または null</returns>
            private BrainStatus GetCurrentEditingBrainStatus()
            {
                // 方法1: Selection.activeObjectから直接取得
                if ( Selection.activeObject is BrainStatus directBrainStatus )
                {
                    return directBrainStatus;
                }

                // 方法2: Selection.objectsから検索取得
                var selectedObjects = Selection.objects;
                foreach ( var obj in selectedObjects )
                {
                    if ( obj is BrainStatus brainStatus )
                    {
                        return brainStatus;
                    }
                }

                // 方法3: 静的キャッシュからフォールバック取得
                // Inspector切り替え時に参照が失われる場合の保険
                return BrainStatusEditorCache.CurrentEditingBrainStatus;
            }

            /// <summary>
            /// 現在のBrainStatusが持つ攻撃データの数を取得
            /// ドロップダウンリストの生成やバリデーションで使用
            /// </summary>
            /// <returns>攻撃データ配列の長さ</returns>
            private int GetCurrentBrainStatusActionCount()
            {
                var brainStatus = GetCurrentEditingBrainStatus();
                return brainStatus.actData.Length;
            }

            /// <summary>
            /// 攻撃データからデフォルトの行動名を生成
            /// actionNameが設定されていない場合の代替表示名を作成
            /// </summary>
            /// <param name="actionData">攻撃データ</param>
            /// <param name="index">配列内のインデックス</param>
            /// <returns>生成されたデフォルト行動名</returns>
            private string GenerateDefaultActionName(ActionData actionData, int index)
            {
                // 行動状態に基づいてベース名を決定
                string baseName = actionData.stateChange switch
                {
                    ActState.攻撃 => "攻撃",
                    ActState.防御 => "防御",
                    ActState.移動 => "移動",
                    ActState.逃走 => "逃走",
                    ActState.支援 => "支援",
                    ActState.回復 => "回復",
                    _ => "行動"  // デフォルト
                };

                return $"{baseName} [{index}]";
            }

            #endregion
        }

        #endregion

        #region エディタ専用プロパティ - CoolTimeData

        /// <summary>
        /// クールタイムデータ構造体のエディタ拡張
        /// 行動のクールタイムスキップ条件を設定するUI
        /// TriggerJudgeDataと類似の構造だが、クールタイム特有の機能を持つ
        /// </summary>
        public partial struct CoolTimeData
        {
            // 以下のプロパティはTriggerJudgeDataと同様の機能
            // クールタイムスキップ条件での使用に特化

            [ShowInInspector]
            [ShowIf("@IsLowerValueBittableBool()")]
            [LabelText("@GetLowerValueLabel()")]
            private BitableBool LowerValueAsBitableBool
            {
                get => (BitableBool)judgeLowerValue;
                set => judgeLowerValue = (int)value;
            }

            [ShowInInspector]
            [ShowIf("@IsLowerValueCharacterBelong()")]
            [LabelText("@GetLowerValueLabel()")]
            private CharacterBelong LowerValueAsCharacterBelong
            {
                get => (CharacterBelong)judgeLowerValue;
                set => judgeLowerValue = (int)value;
            }

            [ShowInInspector]
            [ShowIf("@IsUpperValueRecognizeObject()")]
            [LabelText("@GetUpperValueLabel()")]
            [EnumToggleButtons]
            private RecognizeObjectType UpperValueAsRecognizeObject
            {
                get => (RecognizeObjectType)judgeUpperValue;
                set => judgeUpperValue = (int)value;
            }

            [ShowInInspector]
            [ShowIf("@IsUpperValueBrainEvent()")]
            [LabelText("@GetUpperValueLabel()")]
            [EnumToggleButtons]
            private BrainEventFlagType UpperValueAsBrainEvent
            {
                get => (BrainEventFlagType)judgeUpperValue;
                set => judgeUpperValue = (int)value;
            }

            #region エディタ専用メソッド（CoolTimeData用）

            /// <summary>
            /// スキップ条件の説明文を生成
            /// クールタイムをスキップする条件の詳細を表示
            /// </summary>
            private string GetSkipConditionDescription()
            {
                switch ( skipCondition )
                {
                    case ActTriggerCondition.特定の対象が一定数いる時:
                        return "フィルターに該当する対象が指定範囲の数だけ存在する場合にクールタイムをスキップ";
                    case ActTriggerCondition.HPが一定割合の対象がいる時:
                        return "HPが指定範囲の割合の対象が存在する場合にクールタイムをスキップ";
                    case ActTriggerCondition.MPが一定割合の対象がいる時:
                        return "MPが指定範囲の割合の対象が存在する場合にクールタイムをスキップ";
                    default:
                        return "クールタイムスキップ条件なし";
                }
            }

            /// <summary>
            /// 下限値ラベル生成（CoolTimeData用）
            /// skipConditionに基づいて適切なラベルを返す
            /// </summary>
            private string GetLowerValueLabel()
            {
                switch ( skipCondition )
                {
                    case ActTriggerCondition.周囲に指定のオブジェクトや地形がある時:
                    case ActTriggerCondition.特定のイベントが発生した時:
                        return "判定方法（FALSE:OR判定, TRUE:AND判定）";
                    case ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以上密集している時:
                    case ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以下しかいない時:
                        return "対象陣営";
                    default:
                        return "下限値";
                }
            }

            /// <summary>
            /// 上限値ラベル生成（CoolTimeData用）
            /// skipConditionに基づいて適切なラベルを返す
            /// </summary>
            private string GetUpperValueLabel()
            {
                switch ( skipCondition )
                {
                    case ActTriggerCondition.周囲に指定のオブジェクトや地形がある時:
                        return "オブジェクトタイプ（複数選択可）";
                    case ActTriggerCondition.特定のイベントが発生した時:
                        return "イベントタイプ（複数選択可）";
                    default:
                        return "上限値";
                }
            }

            // UI表示条件判定メソッド群（skipConditionベース）
            private bool ShowLowerValue() => skipCondition != ActTriggerCondition.条件なし;
            private bool ShowUpperValue() => skipCondition != ActTriggerCondition.条件なし;
            private bool IsLowerValueEnum() => IsLowerValueBittableBool() || IsLowerValueCharacterBelong();
            private bool IsUpperValueEnum() => IsUpperValueRecognizeObject() || IsUpperValueBrainEvent();

            private bool IsLowerValueBittableBool()
            {
                return skipCondition == ActTriggerCondition.周囲に指定のオブジェクトや地形がある時 ||
                       skipCondition == ActTriggerCondition.特定のイベントが発生した時;
            }

            private bool IsLowerValueCharacterBelong()
            {
                return skipCondition == ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以上密集している時 ||
                       skipCondition == ActTriggerCondition.対象のキャラの周囲に特定陣営が一定以下しかいない時;
            }

            private bool IsUpperValueRecognizeObject()
            {
                return skipCondition == ActTriggerCondition.周囲に指定のオブジェクトや地形がある時;
            }

            private bool IsUpperValueBrainEvent()
            {
                return skipCondition == ActTriggerCondition.特定のイベントが発生した時;
            }

            #endregion
        }

        #endregion

        #region エディタ専用プロパティ - ActJudgeData

        /// <summary>
        /// 行動判定データ構造体のエディタ拡張
        /// 特定の行動を実行するかどうかの判定条件を設定
        /// MoveSelectConditionを使用してより詳細な行動制御を行う
        /// </summary>
        public partial struct ActJudgeData
        {
            // 基本的な型変換プロパティは他の構造体と同様
            [ShowInInspector]
            [ShowIf("@IsLowerValueBittableBool()")]
            [LabelText("@GetLowerValueLabel()")]
            private BitableBool LowerValueAsBitableBool
            {
                get => (BitableBool)judgeLowerValue;
                set => judgeLowerValue = (int)value;
            }

            [ShowInInspector]
            [ShowIf("@IsLowerValueCharacterBelong()")]
            [LabelText("@GetLowerValueLabel()")]
            private CharacterBelong LowerValueAsCharacterBelong
            {
                get => (CharacterBelong)judgeLowerValue;
                set => judgeLowerValue = (int)value;
            }

            [ShowInInspector]
            [ShowIf("@IsUpperValueRecognizeObject()")]
            [LabelText("@GetUpperValueLabel()")]
            [EnumToggleButtons]
            private RecognizeObjectType UpperValueAsRecognizeObject
            {
                get => (RecognizeObjectType)judgeUpperValue;
                set => judgeUpperValue = (int)value;
            }

            [ShowInInspector]
            [ShowIf("@IsUpperValueBrainEvent()")]
            [LabelText("@GetUpperValueLabel()")]
            [EnumToggleButtons]
            private BrainEventFlagType UpperValueAsBrainEvent
            {
                get => (BrainEventFlagType)judgeUpperValue;
                set => judgeUpperValue = (int)value;
            }

            /// <summary>
            /// 個別行動選択用のドロップダウンプロパティ（ActJudgeData版）
            /// TriggerJudgeDataと同様の機能だが、行動判定専用
            /// </summary>
            [ShowInInspector]
            [ShowIf("@IsIndividualActionMode()")]
            [LabelText("実行する行動")]
            [ValueDropdown("@GetActionDropdownList()")]
            [InfoBox("攻撃データ配列から行動を選択してください。", InfoMessageType.Info, "@IsActionDataEmpty()")]
            private byte TriggerNumAsActionIndex
            {
                get => triggerNum;
                set => triggerNum = value;
            }

            #region エディタ専用メソッド（ActJudgeData用）

            /// <summary>
            /// 行動判定条件の説明文を生成
            /// MoveSelectConditionに基づいた説明を提供
            /// </summary>
            private string GetConditionDescription()
            {
                switch ( judgeCondition )
                {
                    case MoveSelectCondition.対象がフィルターに当てはまる時:
                        return "フィルター条件に該当する対象が存在する時に行動実行";
                    case MoveSelectCondition.対象のHPが一定割合の時:
                        return "対象のHPが指定範囲の割合の時に行動実行";
                    case MoveSelectCondition.対象のMPが一定割合の時:
                        return "対象のMPが指定範囲の割合の時に行動実行";
                    case MoveSelectCondition.ターゲットが自分の場合:
                        return "ターゲットが自分自身の場合に行動実行";
                    default:
                        return "条件なし - 常に行動実行";
                }
            }

            /// <summary>
            /// 下限値ラベル生成（ActJudgeData用）
            /// MoveSelectConditionに基づいてラベルを決定
            /// </summary>
            private string GetLowerValueLabel()
            {
                switch ( judgeCondition )
                {
                    case MoveSelectCondition.対象の周囲に指定のオブジェクトや地形がある時:
                    case MoveSelectCondition.特定のイベントが発生した時:
                        return "判定方法（FALSE:OR判定, TRUE:AND判定）";
                    case MoveSelectCondition.対象の周囲に特定陣営のキャラが一定以上密集している時:
                    case MoveSelectCondition.対象の周囲に特定陣営のキャラが一定以下しかいない時:
                        return "対象陣営";
                    default:
                        return "下限値";
                }
            }

            /// <summary>
            /// 上限値ラベル生成（ActJudgeData用）
            /// MoveSelectConditionに基づいてラベルを決定
            /// </summary>
            private string GetUpperValueLabel()
            {
                switch ( judgeCondition )
                {
                    case MoveSelectCondition.対象の周囲に指定のオブジェクトや地形がある時:
                        return "オブジェクトタイプ（複数選択可）";
                    case MoveSelectCondition.特定のイベントが発生した時:
                        return "イベントタイプ（複数選択可）";
                    default:
                        return "上限値";
                }
            }

            /// <summary>
            /// 下限値フィールドの表示条件判定
            /// 特定の条件では下限値が不要なため非表示にする
            /// </summary>
            private bool ShowLowerValue()
            {
                return judgeCondition != MoveSelectCondition.条件なし &&
                       judgeCondition != MoveSelectCondition.対象がフィルターに当てはまる時 &&
                       judgeCondition != MoveSelectCondition.ターゲットが自分の場合;
            }

            /// <summary>
            /// 上限値フィールドの表示条件判定
            /// 特定の条件では上限値が不要なため非表示にする
            /// </summary>
            private bool ShowUpperValue()
            {
                return judgeCondition != MoveSelectCondition.条件なし &&
                       judgeCondition != MoveSelectCondition.対象がフィルターに当てはまる時 &&
                       judgeCondition != MoveSelectCondition.ターゲットが自分の場合;
            }

            // UI表示条件判定メソッド群
            private bool IsLowerValueEnum() => IsLowerValueBittableBool() || IsLowerValueCharacterBelong();
            private bool IsUpperValueEnum() => IsUpperValueRecognizeObject() || IsUpperValueBrainEvent();

            private bool IsLowerValueBittableBool()
            {
                return judgeCondition == MoveSelectCondition.対象の周囲に指定のオブジェクトや地形がある時 ||
                       judgeCondition == MoveSelectCondition.特定のイベントが発生した時;
            }

            private bool IsLowerValueCharacterBelong()
            {
                return judgeCondition == MoveSelectCondition.対象の周囲に特定陣営のキャラが一定以上密集している時 ||
                       judgeCondition == MoveSelectCondition.対象の周囲に特定陣営のキャラが一定以下しかいない時;
            }

            private bool IsUpperValueRecognizeObject()
            {
                return judgeCondition == MoveSelectCondition.対象の周囲に指定のオブジェクトや地形がある時;
            }

            private bool IsUpperValueBrainEvent()
            {
                return judgeCondition == MoveSelectCondition.特定のイベントが発生した時;
            }

            // 行動選択ドロップダウン関連（ActJudgeData版）
            // TriggerJudgeDataと同じ実装だが、行動判定コンテキストで使用
            private bool IsIndividualActionMode() => triggerEventType == TriggerEventType.個別行動;
            private bool ShowNormalTriggerNum() => !IsIndividualActionMode();
            private bool IsActionDataEmpty() => GetCurrentBrainStatusActionCount() == 0;

            /// <summary>
            /// 行動選択ドロップダウンリスト生成（ActJudgeData版）
            /// TriggerJudgeDataと同じ実装を再利用
            /// </summary>
            private ValueDropdownList<byte> GetActionDropdownList()
            {
                var list = new ValueDropdownList<byte>();
                var brainStatus = GetCurrentEditingBrainStatus();

                // 行動データを先に入れる
                if ( brainStatus != null && brainStatus.actData != null )
                {
                    for ( int i = 0; i < brainStatus.actData.Length; i++ )
                    {
                        var actionData = brainStatus.actData[i];
                        string displayName = string.IsNullOrEmpty(actionData.actionName)
                            ? GenerateDefaultActionName(actionData, i)
                            : actionData.actionName;
                        list.Add(displayName, (byte)i);
                    }
                }

                // 攻撃データが存在する場合は攻撃データも追加
                if ( brainStatus != null && brainStatus.actData != null )
                {
                    for ( int i = 0; i < brainStatus.actData.Length; i++ )
                    {
                        var actionData = brainStatus.actData[i];
                        string displayName = string.IsNullOrEmpty(actionData.actionName)
                            ? GenerateDefaultActionName(actionData, i)
                            : actionData.actionName;
                        list.Add(displayName, (byte)(i + list.Count));
                    }
                }

                if ( list.Count == 0 )
                {
                    list.Add("行動データなし", 0);
                }

                return list;
            }

            /// <summary>
            /// BrainStatus参照取得（ActJudgeData版）
            /// 複数の方法でインスタンス取得を試行
            /// </summary>
            private BrainStatus GetCurrentEditingBrainStatus()
            {
                // 方法1: Selection.activeObjectから取得
                if ( Selection.activeObject is BrainStatus directBrainStatus )
                {
                    return directBrainStatus;
                }

                // 方法2: Selection.objectsから取得
                var selectedObjects = Selection.objects;
                foreach ( var obj in selectedObjects )
                {
                    if ( obj is BrainStatus brainStatus )
                    {
                        return brainStatus;
                    }
                }

                // 方法3: 静的キャッシュから取得（フォールバック）
                return BrainStatusEditorCache.CurrentEditingBrainStatus;
            }

            /// <summary>
            /// 攻撃データ数取得（ActJudgeData版）
            /// </summary>
            private int GetCurrentBrainStatusActionCount()
            {
                var brainStatus = GetCurrentEditingBrainStatus();
                return brainStatus.actData.Length;
            }

            /// <summary>
            /// デフォルト行動名生成（ActJudgeData版）
            /// 攻撃データからわかりやすい表示名を生成
            /// </summary>
            private string GenerateDefaultActionName(ActionData actionData, int index)
            {
                string baseName = actionData.stateChange switch
                {
                    ActState.攻撃 => "攻撃",
                    ActState.防御 => "防御",
                    ActState.移動 => "移動",
                    ActState.逃走 => "逃走",
                    ActState.支援 => "支援",
                    ActState.回復 => "回復",
                    _ => "行動"
                };

                return $"{baseName} [{index}]";
            }

            #endregion
        }

        #endregion

        #region エディタ専用プロパティ - TargetJudgeData

        /// <summary>
        /// ターゲット判定データ構造体のエディタ拡張
        /// AIがターゲットを選択する際の条件を設定
        /// 他の判定データより単純な構造を持つ
        /// </summary>
        public partial struct TargetJudgeData
        {
            #region エディタ専用メソッド（TargetJudgeData用）

            /// <summary>
            /// ターゲット選択条件の説明文を生成
            /// TargetSelectConditionに基づいた詳細説明を提供
            /// </summary>
            private string GetConditionDescription()
            {
                string baseDescription = judgeCondition switch
                {
                    TargetSelectCondition.高度 => "高度を基準にターゲット選択",
                    TargetSelectCondition.HP割合 => "HP割合を基準にターゲット選択",
                    TargetSelectCondition.HP => "HP値を基準にターゲット選択",
                    TargetSelectCondition.自分 => "自分自身をターゲットに設定",
                    TargetSelectCondition.プレイヤー => "プレイヤーをターゲットに設定",
                    TargetSelectCondition.シスターさん => "シスターさんをターゲットに設定",
                    TargetSelectCondition.指定なし_フィルターのみ => "フィルター条件のみでターゲット選択",
                    _ => "ターゲット選択条件"
                };

                return baseDescription;
            }

            /// <summary>
            /// 反転条件の説明文を生成
            /// isInvertフラグに基づいてターゲット選択の反転動作を説明
            /// </summary>
            private string GetInvertDescription()
            {
                if ( isInvert == BitableBool.TRUE )
                {
                    return "反転条件適用: 最大値→最小値、最高→最低に変更されます";
                }
                else
                {
                    return "通常条件: 最大値や最高の対象を選択します";
                }
            }

            #endregion
        }

        #endregion

        #region Validationメソッド

        /// <summary>
        /// インスペクター読み込み時のバリデーションメソッド
        /// </summary>
        private void OnValidate()
        {
            // 各行動データのデータ更新を行う
            for ( int i = 0; i < actData.Length; i++ )
            {
                actData[i].DataUpdate();
            }
        }

        #endregion
    }

    #region 静的キャッシュクラス

    /// <summary>
    /// BrainStatusエディタ用の静的キャッシュクラス
    /// Inspector切り替え時やSelection変更時にBrainStatusの参照を保持
    /// エディタUIの安定性と一貫性を確保するための重要なヘルパークラス
    /// </summary>
    public static class BrainStatusEditorCache
    {
        /// <summary>
        /// 現在編集中のBrainStatusインスタンスの静的キャッシュ
        /// Inspector切り替えやSelection変更で参照が失われることを防ぐ
        /// </summary>
        private static BrainStatus _currentEditingBrainStatus;

        /// <summary>
        /// 現在編集中のBrainStatusインスタンスのプロパティ
        /// nullチェックを含む安全な参照取得を提供
        /// </summary>
        public static BrainStatus CurrentEditingBrainStatus
        {
            get
            {
                // キャッシュされたオブジェクトが破棄されている場合はnullを返す
                // Unity特有のオブジェクト破棄検出ロジック
                if ( _currentEditingBrainStatus != null && _currentEditingBrainStatus == null )
                {
                    _currentEditingBrainStatus = null;
                }
                return _currentEditingBrainStatus;
            }
            set => _currentEditingBrainStatus = value;
        }

        /// <summary>
        /// キャッシュを安全にクリアするメソッド
        /// エディタ終了時やプロジェクト切り替え時の清掃用
        /// </summary>
        public static void ClearCache()
        {
            _currentEditingBrainStatus = null;
        }

        /// <summary>
        /// エディタ初期化時に呼び出される自動初期化メソッド
        /// Unity Editorの起動時にSelection変更イベントを登録
        /// </summary>
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            // Selection変更時にキャッシュを自動更新するイベントを登録
            Selection.selectionChanged += OnSelectionChanged;
        }

        /// <summary>
        /// Selection変更時のコールバックメソッド
        /// 新しく選択されたオブジェクトがBrainStatusの場合、キャッシュを更新
        /// </summary>
        private static void OnSelectionChanged()
        {
            // 選択が変更された時にBrainStatusがあればキャッシュを更新
            if ( Selection.activeObject is BrainStatus brainStatus )
            {
                CurrentEditingBrainStatus = brainStatus;
            }
        }
    }

    #endregion

    #region カスタムエディタ

    /// <summary>
    /// BrainStatusのカスタムエディタクラス
    /// OdinEditorを継承してOdin Inspectorの機能を活用
    /// Unity InspectorでのBrainStatus表示をカスタマイズ
    /// </summary>
    [CustomEditor(typeof(BrainStatus))]
    public class BrainStatusEditor : OdinEditor
    {
        /// <summary>
        /// Inspector GUIの描画をオーバーライド
        /// カスタムヘッダーとヘルプメッセージを追加
        /// </summary>
        public override void OnInspectorGUI()
        {
            // カスタムタイトルの表示
            EditorGUILayout.Space();
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,                          // フォントサイズ拡大
                alignment = TextAnchor.MiddleCenter     // 中央揃え
            };
            EditorGUILayout.LabelField("AI判断データ設定", titleStyle);
            EditorGUILayout.Space();

            // 標準のOdin Inspector描画を実行
            // 上記で定義したすべてのカスタムプロパティとメソッドが適用される
            base.OnInspectorGUI();

            // ヘルプボックスの表示
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "AI判断データの設定画面です。各条件に応じて適切な値を設定してください。\n" +
                "・BitableBool: FALSE=OR判定, TRUE=AND判定\n" +
                "・ビットフラグ: 複数の条件を組み合わせて指定可能\n" +
                "・フィルター: 対象を絞り込むための条件",
                MessageType.Info);
        }
    }

    #endregion
}
#endif