# System: UISystem_Basic
Section: 1 — MVP

## 責務
基本HUD（HP/MP/スタミナ/通貨/ミニマップ）とメニュー画面の表示。

## 依存
- 入力: DataContainer（HP/MP/スタミナ）、CurrencySystem、MapSystem
- 出力: UI表示更新

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| HUDController | 常時表示UIの管理 | Yes |
| PlayerStatusBar | HP/MP/スタミナバー | Yes |
| CompanionStatusBar | 仲間のHP/MPバー（小型） | Yes |
| CurrencyDisplay | 通貨残高表示 | Yes |
| CooperationCooldown | 連携ボタンのクールダウンインジケータ | Yes |
| PauseMenu | ポーズメニュー | Yes |
| InventoryScreen | インベントリUI | Yes |
| EquipmentScreen | 装備変更UI | Yes |
| ShopScreen | ショップUI | Yes |
| LevelUpScreen | レベルアップ振り分けUI | Yes |

## HUDレイアウト
```
┌─────────────────────────────────────────────────────┐
│ [HP████████░░]  [MP████░░░░]  [STA████████]         │
│ [仲間HP████░░]  [仲間MP██░░]                        │
│                                                      │
│                                           [ミニマップ]│
│                                                      │
│                                                      │
│ [連携CD]  [スタンス:攻撃]              [$12,450]     │
└─────────────────────────────────────────────────────┘
```

## UI Toolkit vs uGUI
- HUD（常時更新）: uGUI（パフォーマンス優先）
- メニュー画面: UI Toolkit（レイアウト柔軟性優先）

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| HP/MP/スタミナバー | プレイヤー・仲間のステータス表示 | PlayMode | High |
| 通貨表示 | 残高のリアルタイム表示 | PlayMode | High |
| 連携クールダウン | 連携ボタンの使用可否表示 | PlayMode | High |
| ポーズメニュー | ゲーム一時停止+メニュー | PlayMode | Medium |
| インベントリ画面 | アイテム一覧・使用 | PlayMode | Medium |
| 装備変更画面 | 武器/盾/コア変更UI | PlayMode | Medium |
| ダメージ数字表示 | ヒット時のフローティングダメージ | PlayMode | Low |

## 設計メモ
- UIシーンはAdditive Loadでゲームシーンに重畳
- HPバーの更新はイベント駆動（毎フレームポーリングしない）
- スタミナバーは変動が速いため、SmoothDampで滑らかに追従
