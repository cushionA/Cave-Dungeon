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

        public UnityEvent Saved => onSaved;
        public UnityEvent SaveFailed => onSaveFailed;

        private SaveDataStore Store => _store ??= new SaveDataStore(string.IsNullOrWhiteSpace(saveFolder)
            ? Application.persistentDataPath : System.IO.Path.Combine(Application.persistentDataPath, saveFolder));

        private void Awake() => _interaction = GetComponent<NpcInteraction3D>();

        private void Start()
        {
            if (restoreOnStart) { TryRestoreCurrentSlot(); }
        }

        internal void SetStoreForTest(SaveDataStore store) => _store = store;

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

        public bool TryRestoreCurrentSlot()
        {
            SaveManager saves = GameManager.Saves;
            if (saves == null || saves.LastLoadedSlot != null || saves.HasSaveData(saves.ActiveSlotIndex)
                || gameObject.scene != SceneManager.GetActiveScene() || !TryGetKnight(out ActorRef actor)) { return false; }

            Vector3 position = respawnAnchor.position;
            Quaternion rotation = respawnAnchor.rotation;
            if (!ExpeditionCheckpoint.TryRestoreCurrentSlot(saves, Store, gameObject.scene.path, savePointId,
                out ExpeditionCheckpoint.Resources restored)) { return false; }
            if (!ReferenceEquals(GameManager.Saves, saves) || !TryGetKnight(out ActorRef resumed) || resumed != actor) { return false; }

            PlaceKnight(position, rotation);
            restored.Restore(ref GameManager.Data.GetVitals(actor.Hash));
            return true;
        }

        private bool TryGetKnight(out ActorRef actor)
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
