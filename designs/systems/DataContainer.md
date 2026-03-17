# System: DataContainer
Section: 1 — MVP

## 責務
SoACharaDataDicを中核とするキャラクターデータの一元管理。SourceGeneratorにより情報クラスからSoAコンテナ・アクセサを自動生成する。

## 依存
- 入力: CharacterInfo（ScriptableObject）、BaseCharacter登録
- 出力: ハッシュベースO(1)データアクセス（ref return）

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| SoACharaDataDic | キャラデータのSoAコンテナ（自動生成） | No |
| ContainerSettingAttribute | SourceGenerator用マーカー属性 | No |
| SoASourceGenerator | コンテナ・アクセサ自動生成 | No (Analyzer DLL) |

## インタフェース
```csharp
// GameManager.Data 経由でアクセス
ref CharacterBaseInfo GetBaseInfo(int objectHash);
ref CharacterAtkStatus GetAtkStatus(int objectHash);
ref CharacterDefStatus GetDefStatus(int objectHash);
ref StaminaInfo GetStaminaInfo(int objectHash);
ref MoveStatus GetMoveStatus(int objectHash);
ref EquipmentStatus GetEquipmentStatus(int objectHash);
// ... 他のSoA構造体も同様
int Add(int hash, BaseCharacter managed, ...structs);
void Remove(int hash);
bool TryGetValue(int hash, out int index);
```

## データフロー
```
CharacterInfo (SO) → BaseCharacter.Initialize() → SoACharaDataDic.Add()
                                                    ↓
全システム → GameManager.Data.Get<Struct>(hash) → ref return (コピーなし)
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| SoAコンテナ生成 | SourceGeneratorでコンテナクラス自動生成 | EditMode | High |
| ハッシュ登録・削除 | Add/Remove + swap-back | EditMode | High |
| ref returnアクセス | 各構造体へのO(1)参照取得 | EditMode | High |
| TryGetValue | 存在チェック付きアクセス | EditMode | High |
| Dispose | UnsafeListのメモリ解放 | EditMode | Medium |

## 設計メモ
- アーキテクチャ01に完全準拠。SourceGenerator DLLは `Assets/ODCGenerator/` に配置済み
- UnsafeList<T>を使用するためUnity.Collections依存
- swap-back削除のため、indexはフレーム内でのみ有効（キャッシュ禁止）
