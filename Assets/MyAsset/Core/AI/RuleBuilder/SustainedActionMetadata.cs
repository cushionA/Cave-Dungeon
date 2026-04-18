namespace Game.Core
{
    /// <summary>
    /// Sustained行動の継続時間（paramValue）セマンティクスを記述するメタデータ。
    /// ActionSlot.paramValue は「自然終了条件が満たされなかった場合のタイムアウト時間（秒）」として扱う。
    /// UI側ではこのテーブルから自然終了条件の説明文を取得して表示する。
    /// </summary>
    public static class SustainedActionMetadata
    {
        /// <summary>
        /// paramValue が 0 の場合「無制限（タイムアウトなし／モード切替まで継続）」として扱うための比較しきい値。
        /// 実装側（ActionBase.cs の Sustained 継続時間判定）は `_duration > 0f` の厳密比較だが、
        /// UI 側では FloatField の浮動小数点入力ゆらぎ（0.0001 など）を 0 扱いにするためこの閾値を用いる。
        /// </summary>
        public const float k_UnlimitedDurationThreshold = 0.01f;

        /// <summary>
        /// 指定 Sustained 行動の自然終了条件の日本語説明を返す。UI のツールチップ・ラベル用。
        /// 注意: 現時点で Sustained 系ハンドラに「自然終了条件」の個別実装はほぼ未導入。
        /// 実体は共通タイムアウト（paramValue 秒経過）が主で、実装済みの自然終了条件が確定したらここに反映する。
        /// </summary>
        public static string GetNaturalEndCondition(SustainedAction action)
        {
            switch (action)
            {
                case SustainedAction.MoveToTarget:
                    return "指定地点へ移動（タイムアウトでキャンセル）";
                case SustainedAction.Follow:
                    return "ターゲットを追従（タイムアウトまで継続）";
                case SustainedAction.Retreat:
                    return "ターゲットから距離を取る（タイムアウトまで継続）";
                case SustainedAction.Flee:
                    return "ターゲットと逆方向へ逃走（タイムアウトまで継続）";
                case SustainedAction.Patrol:
                    return "巡回移動（タイムアウトまで継続）";
                case SustainedAction.Guard:
                    return "ガード姿勢を維持（タイムアウトまで継続）";
                case SustainedAction.Flank:
                    return "挟撃位置へ移動（タイムアウトまで継続、ハンドラ未実装）";
                case SustainedAction.ShieldDeploy:
                    return "盾を展開（タイムアウトまで継続、ハンドラ未実装）";
                case SustainedAction.Decoy:
                    return "囮行動（タイムアウトまで継続、ハンドラ未実装）";
                case SustainedAction.Cover:
                    return "遮蔽物利用（タイムアウトまで継続、ハンドラ未実装）";
                case SustainedAction.Stealth:
                    return "潜伏（タイムアウトまで継続、ハンドラ未実装）";
                case SustainedAction.Orbit:
                    return "周回移動（タイムアウトまで継続、ハンドラ未実装）";
                case SustainedAction.MpRecover:
                    return "MP回復専念（タイムアウトまで継続、ハンドラ未実装）";
                default:
                    return "タイムアウトまで継続";
            }
        }

        /// <summary>
        /// paramValue の意味を「タイムアウト時間上限」として人間に伝える文字列。
        /// </summary>
        public static string GetDurationLabel(float paramValue)
        {
            if (paramValue <= k_UnlimitedDurationThreshold)
            {
                return "無制限";
            }
            return paramValue.ToString("F1") + "秒で強制終了";
        }

        /// <summary>
        /// paramValue = 0 は「無制限」として扱われる。
        /// </summary>
        public static bool IsUnlimited(float paramValue)
        {
            return paramValue <= k_UnlimitedDurationThreshold;
        }
    }
}
