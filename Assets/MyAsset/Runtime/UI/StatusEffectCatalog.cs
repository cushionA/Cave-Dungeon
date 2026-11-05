using System;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    [Serializable]
    public class StatusEffectPresentation
    {
        public StatusEffectId id;
        public string displayName;
        public Sprite icon;
        public Color tint = Color.white;
        public GameObject loopPrefab;
        public Vector3 localOffset;
    }

    [CreateAssetMenu(menuName = "Game/Status Effect Catalog")]
    public class StatusEffectCatalog : ScriptableObject
    {
        [SerializeField] private StatusEffectPresentation[] _entries = Array.Empty<StatusEffectPresentation>();

        public bool TryGet(StatusEffectId id, out StatusEffectPresentation presentation)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                StatusEffectPresentation entry = _entries[i];
                if (entry != null && entry.id == id)
                {
                    presentation = entry;
                    return true;
                }
            }

            presentation = null;
            return false;
        }

        internal static string GetShortName(StatusEffectId id)
        {
            return id switch
            {
                StatusEffectId.Poison => "PSN",
                StatusEffectId.Burn => "BRN",
                StatusEffectId.Bleed => "BLD",
                StatusEffectId.Stun => "STN",
                StatusEffectId.Freeze => "ICE",
                StatusEffectId.Paralyze => "PAR",
                StatusEffectId.Slow => "SLW",
                StatusEffectId.Blind => "BLN",
                StatusEffectId.Silence => "SIL",
                StatusEffectId.Weakness => "WEK",
                StatusEffectId.Curse => "CRS",
                StatusEffectId.Confusion => "CNF",
                StatusEffectId.AttackUp => "ATK",
                StatusEffectId.DefenseUp => "DEF",
                StatusEffectId.SpeedUp => "SPD",
                _ => "?"
            };
        }

        internal static Color GetDefaultTint(StatusEffectId id)
        {
            return id switch
            {
                StatusEffectId.AttackUp or StatusEffectId.DefenseUp or StatusEffectId.SpeedUp =>
                    new Color(0.2f, 0.65f, 0.85f),
                StatusEffectId.Poison => new Color(0.4f, 0.65f, 0.2f),
                StatusEffectId.Burn or StatusEffectId.Bleed => new Color(0.8f, 0.3f, 0.2f),
                StatusEffectId.Freeze => new Color(0.3f, 0.7f, 0.9f),
                _ => new Color(0.6f, 0.4f, 0.7f)
            };
        }
    }
}
