// このファイルは廃止されました。
// ForceComplete() は ActionBase に統合されました。
// 既存の AttackActionHandler / CastActionHandler / SustainedActionHandler を直接使用してください。
// .meta ファイルの参照整合性のためファイルは残しています。
namespace Game.Core
{
    // 後方互換のための型エイリアス（既存テストが参照している場合）
    public class RuntimeAttackHandler : AttackActionHandler { }
    public class RuntimeCastHandler : CastActionHandler { }
    public class RuntimeSustainedHandler : SustainedActionHandler { }
}
