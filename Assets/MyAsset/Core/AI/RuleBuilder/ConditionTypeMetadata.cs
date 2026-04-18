namespace Game.Core
{
    /// <summary>
    /// AICondition の UI 表示に必要なウィジェット種別と既定値を提供する。
    /// 実行ロジック（ConditionEvaluator）ではなく UI 側のメタデータ専用。
    /// </summary>
    public static class ConditionTypeMetadata
    {
        /// <summary>
        /// 条件の入力ウィジェット種別。CompanionAISettings のモード詳細ダイアログが参照する。
        /// </summary>
        public enum WidgetKind : byte
        {
            /// <summary>0.0-1.0 のスライダー（HP/MP/スタミナ/アーマー 比率など）</summary>
            Ratio,
            /// <summary>整数入力（カウント、距離[m]、ダメージスコア）</summary>
            Integer,
            /// <summary>CharacterBelong ビットフラグ選択</summary>
            FactionFlags,
            /// <summary>Flag 系 整数入力（オブジェクトビット / イベントビット）</summary>
            IntegerBitmask,
            /// <summary>
            /// 単一選択の Enum（非 [Flags] な enum の生値を operandA に直接格納する）。
            /// 例: ActState は Neutral=0/Running=1/Jumping=2 … の連番値で比較するため
            /// Equal/NotEqual で評価する必要がある（ビットマスクだと評価側と噛み合わない）。
            /// </summary>
            EnumSelect,
            /// <summary>追加入力なし</summary>
            None,
        }

        /// <summary>
        /// 指定条件タイプに対応する入力ウィジェット種別を返す。
        /// </summary>
        public static WidgetKind GetWidgetKind(AIConditionType type)
        {
            switch (type)
            {
                case AIConditionType.HpRatio:
                case AIConditionType.MpRatio:
                case AIConditionType.StaminaRatio:
                case AIConditionType.ArmorRatio:
                    return WidgetKind.Ratio;

                case AIConditionType.Count:
                case AIConditionType.Distance:
                case AIConditionType.DamageScore:
                    return WidgetKind.Integer;

                case AIConditionType.ProjectileNear:
                    // ConditionEvaluator のコメントで「operandA を閾値距離として使う」と明記されている。
                    // ビットマスクではなく単純な距離(Integer)入力として扱う。
                    return WidgetKind.Integer;

                case AIConditionType.NearbyFaction:
                    return WidgetKind.FactionFlags;

                case AIConditionType.ObjectNearby:
                case AIConditionType.EventFired:
                    return WidgetKind.IntegerBitmask;

                case AIConditionType.SelfActState:
                    // ActState は非 [Flags] な連番 enum。単一選択+Equal比較で評価させる。
                    return WidgetKind.EnumSelect;

                case AIConditionType.None:
                default:
                    return WidgetKind.None;
            }
        }

        /// <summary>
        /// 条件タイプの日本語表示名。
        /// </summary>
        public static string GetLabel(AIConditionType type)
        {
            switch (type)
            {
                case AIConditionType.None:           return "(無効)";
                case AIConditionType.Count:          return "対象数";
                case AIConditionType.HpRatio:        return "HP割合";
                case AIConditionType.MpRatio:        return "MP割合";
                case AIConditionType.StaminaRatio:   return "スタミナ割合";
                case AIConditionType.ArmorRatio:     return "アーマー割合";
                case AIConditionType.Distance:       return "距離[m]";
                case AIConditionType.NearbyFaction:  return "周囲の陣営";
                case AIConditionType.ProjectileNear: return "近接飛翔体";
                case AIConditionType.ObjectNearby:   return "周囲のオブジェクト";
                case AIConditionType.DamageScore:    return "ダメージスコア";
                case AIConditionType.EventFired:     return "イベントフラグ";
                case AIConditionType.SelfActState:   return "自身の行動状態";
                default:                             return "不明";
            }
        }

        /// <summary>
        /// 条件タイプの説明（ツールチップ表示用）。
        /// </summary>
        public static string GetDescription(AIConditionType type)
        {
            switch (type)
            {
                case AIConditionType.None:
                    return "条件を無効化します（常に true）";
                case AIConditionType.Count:
                    return "フィルターに合致する対象の数と比較します";
                case AIConditionType.HpRatio:
                    return "対象のHP割合(0.0-1.0)と比較します";
                case AIConditionType.MpRatio:
                    return "対象のMP割合(0.0-1.0)と比較します";
                case AIConditionType.StaminaRatio:
                    return "対象のスタミナ割合(0.0-1.0)と比較します";
                case AIConditionType.ArmorRatio:
                    return "対象のアーマー割合(0.0-1.0)と比較します";
                case AIConditionType.Distance:
                    return "対象との距離[m]と比較します";
                case AIConditionType.NearbyFaction:
                    return "周囲にいる陣営ビットフラグをチェックします";
                case AIConditionType.ProjectileNear:
                    return "近くに飛翔体が存在するかチェックします";
                case AIConditionType.ObjectNearby:
                    return "周囲の認識対象オブジェクトのビットフラグをチェックします";
                case AIConditionType.DamageScore:
                    return "対象から受けた累積ダメージスコアと比較します";
                case AIConditionType.EventFired:
                    return "AI イベントフラグ（被弾/回避成功 等）のビットをチェックします";
                case AIConditionType.SelfActState:
                    return "自分の ActState のビットをチェックします";
                default:
                    return "";
            }
        }

        /// <summary>
        /// 条件タイプの既定値を生成する（operandA のみ）。
        /// Ratio 系は 0.5 をスケール（50%=50）して返す。
        /// </summary>
        public static int GetDefaultOperandA(AIConditionType type)
        {
            switch (type)
            {
                case AIConditionType.HpRatio:
                case AIConditionType.MpRatio:
                case AIConditionType.StaminaRatio:
                case AIConditionType.ArmorRatio:
                    // UI側ではスライダーで 0.0-1.0 を扱う想定。
                    // 内部表現は 0-100 のint（パーセント）にしておくとエッジケース(0/1)を扱いやすい。
                    return 50;
                case AIConditionType.Count:
                    return 1;
                case AIConditionType.Distance:
                    return 5;
                case AIConditionType.ProjectileNear:
                    // 飛翔体検出の既定閾値距離（3m 以内に飛翔体があるか）
                    return 3;
                case AIConditionType.DamageScore:
                    return 100;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// この条件タイプが比較演算子(CompareOp)による選択をサポートするか。
        /// IntegerBitmask/FactionFlags は HasFlag/HasAny の2択のみ。
        /// Ratio/Integer は Less/LessEqual/Equal/GreaterEqual/Greater/NotEqual。
        /// None は非表示。
        /// </summary>
        public static bool SupportsNumericCompare(AIConditionType type)
        {
            WidgetKind kind = GetWidgetKind(type);
            return kind == WidgetKind.Ratio || kind == WidgetKind.Integer;
        }

        /// <summary>
        /// ビットフラグ系の条件か？
        /// </summary>
        public static bool IsBitmask(AIConditionType type)
        {
            WidgetKind kind = GetWidgetKind(type);
            return kind == WidgetKind.FactionFlags || kind == WidgetKind.IntegerBitmask;
        }

        /// <summary>
        /// IntegerBitmask 系条件のビットごとの日本語ラベル一覧。
        /// ゲーム側で RecognizeObjectType / BrainEventFlagType が enum 定義されたら差し替える。
        /// 配列の長さ = 表示するチェックボックス数。
        /// </summary>
        public static string[] GetBitLabels(AIConditionType type)
        {
            switch (type)
            {
                case AIConditionType.ObjectNearby:
                    return new string[]
                    {
                        "認識対象 1", "認識対象 2", "認識対象 3", "認識対象 4",
                        "認識対象 5", "認識対象 6", "認識対象 7", "認識対象 8",
                        "認識対象 9", "認識対象 10", "認識対象 11", "認識対象 12",
                    };

                case AIConditionType.EventFired:
                    return new string[]
                    {
                        "イベント 1", "イベント 2", "イベント 3", "イベント 4",
                        "イベント 5", "イベント 6", "イベント 7", "イベント 8",
                    };

                default:
                    return new string[0];
            }
        }

        /// <summary>
        /// 条件タイプの既定の CompareOp。
        /// 種類切替時のデフォルト初期化用（UI 側）。
        /// </summary>
        public static CompareOp GetDefaultCompareOp(AIConditionType type)
        {
            WidgetKind kind = GetWidgetKind(type);
            switch (kind)
            {
                case WidgetKind.Ratio:
                case WidgetKind.Integer:
                    return CompareOp.LessEqual;
                case WidgetKind.FactionFlags:
                case WidgetKind.IntegerBitmask:
                    return CompareOp.HasAny;
                case WidgetKind.EnumSelect:
                    return CompareOp.Equal;
                default:
                    return CompareOp.Equal;
            }
        }
    }
}
