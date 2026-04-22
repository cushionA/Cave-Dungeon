using System.Runtime.CompilerServices;

// GameCode asmdef の internal 型を EditMode テストから参照可能にする。
// 主に CompanionAISettingsController.Dialogs の internal テストフック
// (RebuildUnifiedActionList 等) の回帰テスト用。
[assembly: InternalsVisibleTo("Game.Tests.EditMode")]
[assembly: InternalsVisibleTo("Game.Tests.PlayMode")]
