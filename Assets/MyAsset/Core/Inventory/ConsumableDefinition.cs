using System;
using UnityEngine;

namespace Game.Core
{
    [CreateAssetMenu(fileName = "Consumable", menuName = "Game/Items/Consumable")]
    public sealed class ConsumableDefinition : ScriptableObject
    {
        [SerializeField, Min(0)] private int itemId;
        [SerializeField] private string displayName;
        [SerializeField] private ActionDefinition useAction;
        [SerializeField, Min(1)] private int basePower = 20;
        [SerializeField] private StatType scalingStat;
        [SerializeField, Min(0f)] private float statScale;
        [SerializeField] private bool consumeOnUse = true;
        [SerializeField, Min(0), Tooltip("効果発揮時に消費するMP。個数の消費とは独立して設定する。")]
        private int mpCost;
        [SerializeField, Min(1)] private int maxStack = 99;
        [SerializeField] private GameObject effectPrefab;
        [SerializeField] private Vector3 effectOffset;
        [SerializeField, Min(0.01f)] private float effectLifetime = 1.25f;

        public int ItemId => itemId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public ActionDefinition UseAction => useAction;
        public int BasePower => basePower;
        public StatType ScalingStat => scalingStat;
        public float StatScale => statScale;
        public bool ConsumeOnUse => consumeOnUse;
        public int MpCost => mpCost;
        public int MaxStack => maxStack;
        public GameObject EffectPrefab => effectPrefab;
        public Vector3 EffectOffset => effectOffset;
        public float EffectLifetime => effectLifetime;

        public static int CalculatePower(int basePower, StatType scalingStat, float statScale, in StatModifier attributes)
        {
            int attribute = scalingStat switch
            {
                StatType.Str => attributes.str,
                StatType.Dex => attributes.dex,
                StatType.Intel => attributes.intel,
                StatType.Vit => attributes.vit,
                StatType.Mnd => attributes.mnd,
                StatType.End => attributes.end,
                _ => 0,
            };
            double power = basePower + (double)attribute * statScale;
            return (int)Math.Min(int.MaxValue, Math.Max(1d, Math.Round(power, MidpointRounding.AwayFromZero)));
        }
    }
}
