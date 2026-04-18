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
                    return "対象のHP割合(0-100%)と比較します";
                case AIConditionType.MpRatio:
                    return "対象のMP割合(0-100%)と比較します";
                case AIConditionType.StaminaRatio:
                    return "対象のスタミナ割合(0-100%)と比較します";
                case AIConditionType.ArmorRatio:
                    return "対象のアーマー割合(0-100%)と比較します";
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
                    // 評価側(ConditionEvaluator) が current/max * 100f を返すので operandA も 0-100 の int(%) で扱う。
                    // UI もスライダー(0-100)で統一。
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
        /// 配列の長さ = 表示するチェックボックス数。
        /// ラベルは Architect/05_AIシステム.md および Architect/参考コード/AIコード/BrainStatus.cs に定義された
        /// RecognizeObjectType / BrainEventFlagType の項目に準拠する（本家 enum が Game.Core に昇格したら差し替える）。
        /// </summary>
        public static string[] GetBitLabels(AIConditionType type)
        {
            switch (type)
            {
                case AIConditionType.ObjectNearby:
                    // Architect/参考コード BrainStatus.cs:230-246 の RecognizeObjectType (12bit) 準拠。
                    // ゲーム実装側で enum 定義されたら切り替える。
                    return new string[]
                    {
                        "アイテム",         // bit0
                        "プレイヤー陣営",   // bit1
                        "敵陣営",           // bit2
                        "同類キャラ",       // bit3
                        "危険物",           // bit4
                        "バフエリア",       // bit5
                        "デバフエリア",     // bit6
                        "草",               // bit7
                        "焚火",             // bit8
                        "ダメージエリア",   // bit9
                        "破壊可オブジェクト", // bit10
                        "よじ登りポイント", // bit11
                    };

                case AIConditionType.EventFired:
                    // Architect/05_AIシステム.md:803-812 の BrainEventFlagType (6 定義 + 2 予約) 準拠。
                    return new string[]
                    {
                        "大ダメージを与えた", // bit0
                        "大ダメージを受けた", // bit1
                        "キャラを倒した",     // bit2
                        "回復を使用",         // bit3
                        "支援を使用",         // bit4
                        "攻撃対象指示",       // bit5
                        "(予約 bit6)",        // bit6
                        "(予約 bit7)",        // bit7
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
