using System;
using ODC.Attributes;

namespace Game.Core
{
    [ContainerSetting(
        structType: new[]
        {
            typeof(CharacterVitals),
            typeof(CombatStats),
            typeof(CharacterFlags),
            typeof(MoveParams),
            typeof(EquipmentStatus),
            typeof(CharacterStatusEffects),
            typeof(AnimationStateData)
        },
        classType: new[] { typeof(ManagedCharacter) })]
    public partial class SoACharaDataDic : IDisposable
    {
        public partial void Dispose();
    }
}
