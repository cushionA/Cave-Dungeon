using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Runtime
{
    public sealed class StatusEffectPresentationController : IDisposable
    {
        private readonly SoACharaDataDic _data;
        private readonly StatusEffectStore _store;
        private readonly StatusEffectCatalog _catalog;
        private readonly List<ActiveEffect> _effects = new List<ActiveEffect>();
        private readonly Dictionary<(int hash, int instanceId), GameObject> _loops = new();
        private readonly HashSet<(int hash, int instanceId)> _visible = new();
        private readonly List<(int hash, int instanceId)> _removed = new();

        public StatusEffectPresentationController(SoACharaDataDic data, StatusEffectStore store,
            StatusEffectCatalog catalog)
        {
            _data = data;
            _store = store;
            _catalog = catalog;
        }

        public void Sync()
        {
            _visible.Clear();
            _effects.Clear();
            if (_catalog != null)
            {
                _store.CopyActiveEffects(_effects);
            }
            for (int i = 0; i < _effects.Count; i++)
            {
                ActiveEffect effect = _effects[i];
                int hash = effect.targetHash;
                if (!_data.TryGetValue(hash, out _))
                {
                    continue;
                }
                ManagedCharacter character = _data.GetManaged(hash);
                if (character == null || !character.gameObject.activeInHierarchy
                    || _data.GetFlags(hash).ActState == ActState.Dead)
                {
                    continue;
                }

                CharacterVitals vitals = _data.GetVitals(hash);
                if (vitals.maxHp > 0 && vitals.currentHp <= 0)
                {
                    continue;
                }

                if (!_catalog.TryGet(effect.id, out StatusEffectPresentation presentation)
                    || presentation.loopPrefab == null)
                {
                    continue;
                }

                var key = (hash, effect.instanceId);
                _visible.Add(key);
                if (_loops.TryGetValue(key, out GameObject loop))
                {
                    if (loop != null && loop.transform.parent == character.transform)
                    {
                        continue;
                    }
                    Release(loop);
                }

                loop = Object.Instantiate(presentation.loopPrefab, character.transform, false);
                loop.transform.localPosition = presentation.localOffset;
                loop.SetActive(true);
                _loops[key] = loop;
            }

            _removed.Clear();
            foreach (KeyValuePair<(int hash, int instanceId), GameObject> pair in _loops)
            {
                if (!_visible.Contains(pair.Key))
                {
                    Release(pair.Value);
                    _removed.Add(pair.Key);
                }
            }

            for (int i = 0; i < _removed.Count; i++)
            {
                _loops.Remove(_removed[i]);
            }
        }

        public void Dispose()
        {
            foreach (GameObject loop in _loops.Values)
            {
                Release(loop);
            }
            _loops.Clear();
            _effects.Clear();
            _visible.Clear();
            _removed.Clear();
        }

        private static void Release(GameObject loop)
        {
            if (loop == null)
            {
                return;
            }
            loop.SetActive(false);
            if (Application.isPlaying)
            {
                Object.Destroy(loop);
            }
            else
            {
                Object.DestroyImmediate(loop);
            }
        }
    }
}
