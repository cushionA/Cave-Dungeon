using Game.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>騎士が操作した地点で遠征を保存し、起動時にその地点から再開する。</summary>
    [DefaultExecutionOrder(-40)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcInteraction3D))]
    public sealed class ExpeditionSavePoint3D : MonoBehaviour
    {
        [SerializeField] private string savePointId;
        [SerializeField] private CharacterActor3D knight;
        [SerializeField] private Transform respawnAnchor;
        [SerializeField] private string saveFolder = "ExpeditionMock3D";
        [SerializeField] private bool restoreOnStart;
        [SerializeField] private UnityEvent onSaved = new UnityEvent();
        [SerializeField] private UnityEvent onSaveFailed = new UnityEvent();

        private NpcInteraction3D _interaction;
        private SaveDataStore _store;
        private SaveManager _startupSaves;
        private int _startupSlot;

        public UnityEvent Saved => onSaved;
        public UnityEvent SaveFailed => onSaveFailed;
        internal string SavePointId => savePointId;

        internal SaveDataStore Store => _store ??= new SaveDataStore(string.IsNullOrWhiteSpace(saveFolder)
            ? Application.persistentDataPath : System.IO.Path.Combine(Application.persistentDataPath, saveFolder));

        private void Awake() => _interaction = GetComponent<NpcInteraction3D>();

        private void Start()
        {
            if (!restoreOnStart) { return; }
            if (gameObject.scene == SceneManager.GetActiveScene())
            {
                TryRestoreCurrentSlot();
                return;
            }

            // 加算ロードではシーンが有効になった時点で、起動時のスロットを一度だけ試す。
            SaveManager saves = GameManager.Saves;
            if (saves == null || saves.LastLoadedSlot != null || saves.HasSaveData(saves.ActiveSlotIndex)) { return; }
            _startupSaves = saves;
            _startupSlot = saves.ActiveSlotIndex;
            SceneManager.activeSceneChanged += RestoreOnActivation;
        }

        private void OnDisable() => StopWaitingForActivation();

        private void RestoreOnActivation(Scene _, Scene current)
        {
            SaveManager saves = _startupSaves;
            if (saves == null || current != gameObject.scene) { return; }
            StopWaitingForActivation();
            if (saves == GameManager.Saves && saves.ActiveSlotIndex == _startupSlot) { TryRestoreCurrentSlot(); }
        }

        private void StopWaitingForActivation()
        {
            if (_startupSaves == null) { return; }
            SceneManager.activeSceneChanged -= RestoreOnActivation;
            _startupSaves = null;
        }

        internal void UseSaveStore(SaveDataStore store) => _store = store;
        internal void SetStoreForTest(SaveDataStore store) => UseSaveStore(store);

        public void SaveCurrentSlot()
        {
            if (TrySaveCurrentSlot()) { onSaved.Invoke(); }
            else { onSaveFailed.Invoke(); }
        }

        public bool TrySaveCurrentSlot()
        {
            if (!TryGetKnight(out ActorRef actor) || !_interaction.TryGetDistanceSquared(knight, out _)) { return false; }
            SaveManager saves = GameManager.Saves;
            CharacterVitals vitals = GameManager.Data.GetVitals(actor.Hash);
            if (!ExpeditionCheckpoint.TrySaveCurrentSlot(saves, Store, gameObject.scene.path, savePointId, vitals,
                out ExpeditionCheckpoint.Resources recovered)) { return false; }

            if (ReferenceEquals(GameManager.Saves, saves) && knight != null && knight.Actor == actor && knight.IsAlive)
            {
                recovered.ApplyRecovery(ref GameManager.Data.GetVitals(actor.Hash));
                GameManager.Events?.FireSavePointUsed(savePointId);
            }
            return true;
        }

        internal bool TryAutosaveCurrentSlot(ActorRef expectedActor, ExpeditionCheckpoint.AutosaveTicket? ticket = null,
            System.Func<bool> canCommit = null)
        {
            if (!TryGetKnight(out ActorRef actor) || actor != expectedActor) { return false; }
            CharacterVitals vitals = GameManager.Data.GetVitals(actor.Hash);
            if (ticket.HasValue) { return ticket.Value.TryWrite(gameObject.scene.path, savePointId, vitals, canCommit); }
            return ExpeditionCheckpoint.TryAutosaveCurrentSlot(GameManager.Saves, Store,
                gameObject.scene.path, savePointId, vitals);
        }

        public bool TryRestoreCurrentSlot()
        {
            SaveManager saves = GameManager.Saves;
            if (saves == null || saves.LastLoadedSlot != null || saves.HasSaveData(saves.ActiveSlotIndex)
                || gameObject.scene != SceneManager.GetActiveScene()) { return false; }
            return RestoreKnight(saves, null);
        }

        internal bool TryRestoreFromTitle(SaveManager saves, ExpeditionSlot launch) => RestoreKnight(saves, launch);

        private bool RestoreKnight(SaveManager saves, ExpeditionSlot? launch)
        {
            if (!TryGetKnight(out ActorRef actor)) { return false; }

            CharacterActor3D owner = knight;
            Vector3 position = respawnAnchor.position;
            Quaternion rotation = respawnAnchor.rotation;
            bool Place(ExpeditionCheckpoint.Resources resources)
            {
                if (!ReferenceEquals(GameManager.Saves, saves) || knight != owner
                    || !TryGetKnight(out ActorRef resumed) || resumed != actor) { return false; }
                PlaceKnight(position, rotation);
                resources.Restore(ref GameManager.Data.GetVitals(actor.Hash));
                return true;
            }

            string scenePath = gameObject.scene.path;
            ExpeditionCheckpoint.Resources restored;
            bool loaded = launch.HasValue
                ? ExpeditionLaunch.TryRestoreCheckpoint(saves, launch.Value, scenePath, savePointId, out restored, Place)
                : ExpeditionCheckpoint.TryRestoreCurrentSlot(saves, Store, scenePath, savePointId, out restored, Place);
            // Loaded observers apply growth and equipment caps after the checkpoint has been placed.
            if (loaded && ReferenceEquals(GameManager.Saves, saves) && knight == owner && TryGetKnight(out ActorRef current))
            {
                restored.Restore(ref GameManager.Data.GetVitals(current.Hash));
            }
            return loaded;
        }

        internal bool TryGetKnight(out ActorRef actor)
        {
            actor = default;
            if (!isActiveAndEnabled || _interaction == null || _interaction.InteractionType != InteractionType.SavePoint
                || knight == null || !knight.isActiveAndEnabled || !knight.IsAlive || knight.gameObject.scene != gameObject.scene
                || knight.Motor == null || !knight.Motor.isActiveAndEnabled || knight.Body == null || !knight.Body.enabled
                || respawnAnchor == null || respawnAnchor.gameObject.scene != gameObject.scene)
            {
                return false;
            }
            Vector3 position = respawnAnchor.position;
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z)) { return false; }

            actor = knight.Actor;
            return (GameManager.Data.GetFlags(actor.Hash).Feature & CharacterFeature.Player) != 0;
        }

        private void PlaceKnight(Vector3 position, Quaternion rotation)
        {
            CharacterMotor3D motor = knight.Motor;
            CharacterController body = knight.Body;
            motor.enabled = false;
            body.enabled = false;
            try { knight.transform.SetPositionAndRotation(position, rotation); }
            finally
            {
                body.enabled = true;
                motor.enabled = true;
            }
            Physics.SyncTransforms();
        }
    }
}
