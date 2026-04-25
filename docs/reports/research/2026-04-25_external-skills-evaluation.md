# 外部 skill / agent 輸入候補の評価 (2026-04-25)

**調査対象**: Wave 3 Phase 22 P22-T5〜T7（FUTURE_TASKS 既登録）
**目的**: SisterGame に輸入可能な外部 skill / agent を評価し、採用候補リストを作る

## 評価基準

1. **ライセンス**: MIT / Apache 2.0 等の商用 OK ライセンスのみ
2. **C# 9 規約整合性**: SisterGame の `.claude/rules/unity-conventions.md` と矛盾しないか
3. **Architect/ 整合性**: SoA + GameManager 中央ハブ + Ability 拡張の設計と衝突しないか
4. **Skill 過多回避**: `Shrivu Shankar 「20+ slash command はアンチパターン」` を踏まえ、本当に必要なものだけ
5. **メンテ性**: 公式 Anthropic の 2 段階吸収サイクル（コミュニティ → 公式吸収）に乗っている skill は採用慎重

## 候補リポジトリ

### 1. [wshobson/agents](https://github.com/wshobson/agents) — 184 agents + 150 skills + 98 commands
- **ライセンス**: MIT
- **規模**: 巨大（全 184 agents）
- **SisterGame 適合度**: 低〜中
  - Unity 特化 agent はなし
  - 汎用 agent（code-reviewer, architect 等）は既に SisterGame で reviewer-optimizer / reviewer-skeptic として実装済み（PR #57）
  - **新規輸入よりも、独立検証 subagent としての参考実装に**
- **採用候補**: なし（独自実装で十分）

### 2. [rohitg00/awesome-claude-code-toolkit](https://github.com/rohitg00/awesome-claude-code-toolkit) — 135 agents + 35 skills
- **ライセンス**: MIT
- **規模**: 中
- **SisterGame 適合度**: 低
  - Web/SaaS 系 agent が多く、Unity ゲーム開発との親和性は低い
- **採用候補**: なし

### 3. [anthropics/skills](https://github.com/anthropics/skills) — 公式 37.5k★
- **ライセンス**: 公式
- **既導入**: anthropic-skills:* として globally インストール済（docx / pptx / xlsx / pdf / consolidate-memory / skill-creator / setup-cowork / unicli 等）
- **採用候補**:
  - ✅ **既に Cowork で利用可能、追加導入不要**
  - 注意: anthropic-skills:unicli が SisterGame の自前 unicli と name 衝突するが、namespace で分離されているので併存 OK

### 4. [The1Studio/theone-training-skills](https://github.com/The1Studio/theone-training-skills) — Unity 特化
- **ライセンス**: 要確認（README 未確認）
- **SisterGame 適合度**: 高（Unity 特化）
- **C# 9 規約**: 要確認 — TheOne Studio が独自規約を持つ場合、unity-conventions.md と衝突可能
- **採用候補**: ⚠️ **保留** — README 精読 + ライセンス確認後に再評価
- **アクション**: Wave 5 着手時 or 別セッションで詳細調査

### 5. [Unity App UI Plugin (公式)](https://docs.unity3d.com/Packages/com.unity.dt.app-ui@2.2/manual/claude-plugin.html)
- **ライセンス**: Unity 公式（プロジェクトでの利用許諾あり）
- **SisterGame 適合度**: 不明
  - SisterGame は UI Toolkit + uGUI 併用（`.claude/skills/create-ui/` 既存）
  - Unity App UI は新しい UI フレームワーク、現状の create-ui SKILL と統合 or 置換が必要
- **採用候補**: ⚠️ **保留** — 別 PR で UI 戦略全体を見直すタイミングで判断

### 6. [unity-dev-toolkit](#) — 66 skills（出典: WAVE_PLAN.md L157）
- **ライセンス**: 要確認
- **SisterGame 適合度**: 不明
- **採用候補**: ⚠️ **保留**

### 7. [ComposioHQ/awesome-claude-skills](https://github.com/ComposioHQ/awesome-claude-skills) — 外部 API 系
- **SisterGame 適合度**: 低（外部 SaaS API 連携が中心、Unity 開発と関係薄）
- **採用候補**: なし

## 結論

### ✅ 採用（追加作業不要）

- **anthropic-skills:* (Cowork 経由)** — 既に利用可能。明示的な輸入作業は不要

### ⚠️ 保留 → 詳細調査結果（2026-04-25 追記、ユーザー「輸入もいいよ」を受けて再評価）

ユーザーの輸入意思を受けて両候補を WebFetch で精査:

#### TheOne Studio skills (theone-unity-standards) — ❌ 見送り

- **規約**: VContainer / SignalBus / Data Controllers / UniTask / TheOne.Logging を**強制**
- **SisterGame との衝突**:
  - SisterGame は自前 GameManager 中央ハブ（VContainer ではない）
  - SoA + ODCGenerator アーキテクチャ（独自）
  - 独自 logging (`AILogger`) → TheOne.Logging と衝突
- **判定**: 既存 Architect/ 設計を**根本から書き換える**必要があり、輸入コスト >> 利益
- **代替**: SisterGame 独自の `.claude/rules/architecture.md` で十分

#### Unity App UI Plugin — ❌ 見送り

- **対象**: Unity App UI Package（com.unity.dt.app-ui）専用の 5 skill
- **SisterGame との衝突**:
  - SisterGame は UI Toolkit (UXML/USS) + uGUI 併用
  - Unity App UI Package は別フレームワーク、導入には UI 全体再構築が必要
- **判定**: UI 戦略全体の見直し（ゲーム UI を全部 App UI で書き換え）が前提となるため、現フェーズでは見送り
- **代替**: 既存 `create-ui` SKILL（UI Toolkit + uGUI）で必要十分

### 結論（2026-04-25 最終）

ユーザー「外部スキル輸入もいいよ」の意思は受け取ったが、**適合候補がないため本セッションでは輸入を実施しない**。

| 候補 | 結果 | 理由 |
|------|------|------|
| anthropic-skills:* | ✅ 既導入（Cowork 経由） | 追加作業不要 |
| TheOne Studio | ❌ 見送り | アーキテクチャ衝突 |
| Unity App UI Plugin | ❌ 見送り | UI フレームワーク衝突 |
| wshobson/agents | ❌ 見送り | 巨大すぎ + Unity 非対応 |
| rohitg00/awesome-toolkit | ❌ 見送り | Web/SaaS 寄り |
| ComposioHQ/awesome-claude-skills | ❌ 見送り | 外部 SaaS API 寄り |

**継続アクション**:
- Anthropic 公式リリース月次監視（FUTURE_TASKS の Phase 9 ⏸ エントリで管理）
- 適合する skill が登場したら本ドキュメントに追記し、別 PR で輸入

### ❌ 採用見送り

- wshobson/agents — 巨大すぎ + Unity 非対応、独自 reviewer agent で十分
- rohitg00/awesome-claude-code-toolkit — Web/SaaS 寄りで Unity 親和性低
- ComposioHQ/awesome-claude-skills — 外部 SaaS API 連携中心

## 採用方針（Anthropic 2 段階吸収サイクル前提）

`docs/WAVE_PLAN.md` v2 の「外部スキルはライフサイクル短め前提」原則に従い:

1. **明示的に必要な機能がない限り、外部 skill は輸入しない**
2. 公式 Anthropic skill / Cowork 経由で得られる機能は優先採用
3. SisterGame 固有の機能は自前実装（Wave 2-4 で実証済）
4. Anthropic 月次リリースを Wave 5 以降で監視（FUTURE_TASKS の Phase 9 ⏸ エントリ）

## アクション

- [x] 評価結果を本ドキュメントに保存
- [x] 採用見送り 3 候補は今後候補リストから外す
- [ ] TheOne Studio / Unity App UI Plugin は別セッションで個別調査（FUTURE_TASKS 残）
- [ ] Anthropic リリースノート月次監視（Phase 9 ⏸）

## 出典

- [wshobson/agents](https://github.com/wshobson/agents)
- [rohitg00/awesome-claude-code-toolkit](https://github.com/rohitg00/awesome-claude-code-toolkit)
- [anthropics/skills](https://github.com/anthropics/skills)
- [The1Studio/theone-training-skills](https://github.com/The1Studio/theone-training-skills)
- [Unity App UI Plugin](https://docs.unity3d.com/Packages/com.unity.dt.app-ui@2.2/manual/claude-plugin.html)
- [ComposioHQ/awesome-claude-skills](https://github.com/ComposioHQ/awesome-claude-skills)
- WAVE_PLAN.md L148-158 (Phase 9 ⏸ — 外部スキル組み込み)
