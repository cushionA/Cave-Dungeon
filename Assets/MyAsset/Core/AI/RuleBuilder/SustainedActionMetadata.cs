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
        /// paramValue = 0 を「無制限（モード切替まで継続）」と解釈するしきい値。
        /// </summary>
        public const float k_UnlimitedDurationThreshold = 0.01f;

        /// <summary>
        /// 指定 Sustained 行動の自然終了条件の日本語説明を返す。
        /// UI のツールチップやラベル表示用。
        /// </summary>
        public static string GetNaturalEndCondition(SustainedAction action)
        {
            switch (action)
            {
                case SustainedAction.MoveToTarget:
                    return "目的地到達で自動終了";
                case SustainedAction.Follow:
                    return "ターゲットロスト/モード変更まで継続";
                case SustainedAction.Retreat:
                    return "安全距離（3-5m程度）まで下がったら停止。戦闘態勢は保持（間合い調整向け）";
                case SustainedAction.Flee:
                    return "追跡者を振り切るまで全力で逃走。戦闘放棄（緊急退避向け）";
                case SustainedAction.Patrol:
                    return "ターゲット発見で自動終了";
                case SustainedAction.Guard:
                    return "モード変更/別行動開始まで継続";
                case SustainedAction.Flank:
                    return "挟撃位置到達で自動終了";
                case SustainedAction.ShieldDeploy:
                    return "怯み/MP切れで中断";
                case SustainedAction.Decoy:
                    return "ターゲットを引きつけるまで継続";
                case SustainedAction.Cover:
                    return "遮蔽物到達で継続(モード変更まで)";
                case SustainedAction.Stealth:
                    return "被発見/攻撃で終了";
                case SustainedAction.Orbit:
                    return "モード変更まで継続";
                case SustainedAction.MpRecover:
                    return "MP満タン/怯みで中断";
                default:
                    return "モード変更まで継続";
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
