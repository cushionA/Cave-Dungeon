using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.Runtime
{
    [RequireComponent(typeof(Animator))]
    public class CharacterAnimationController : MonoBehaviour
    {
        private sealed class MotionLayer
        {
            public long instanceId;
            public int inputIndex = -1;
            public int animatorLayer;
            public int priority;
            public float blendStartElapsed;
            public float blendDuration;
            public float weight;
            public AvatarMask mask;
            public AnimationClip clip;
            public AnimationClipPlayable playable;
        }

        private readonly Dictionary<long, MotionLayer> _layers = new Dictionary<long, MotionLayer>();
        private readonly List<MotionLayer> _orderedLayers = new List<MotionLayer>();
        private Animator _animator;
        private AnimationBridge _bridge;
        private int _ownerHash;
        private bool _isInitialized;
        private PlayableGraph _graph;
        private AnimationPlayableOutput _output;
        private AnimationLayerMixerPlayable _mixer;
        private AnimatorControllerPlayable _baseController;

        public AnimationBridge Bridge => _bridge;
        public int ActiveMotionCount => _layers.Count;
        public bool HasActiveGraph => _graph.IsValid();

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _bridge = new AnimationBridge();
        }

        private void OnDisable() { ReleaseGraph(); }
        private void OnDestroy() { ReleaseGraph(); }

        public void Initialize(int ownerHash)
        {
            _ownerHash = ownerHash;
            _isInitialized = true;
        }

        public void SetExecutionState(AnimationStateData state)
        {
            _bridge.ApplyExecutionState(state);
            SyncToSoA();
        }

        public bool HasMotion(long instanceId) => _layers.ContainsKey(instanceId);

        public void Present(ActionExecution execution, MotionDefinition asset, MotionInfo snapshot)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }
            AnimationClip clip = SelectClip(execution.phase, snapshot);
            if (clip == null)
            {
                EndExecution(execution.instanceId);
                return;
            }

            EnsureGraph();
            bool rebuild = false;
            if (!_layers.TryGetValue(execution.instanceId, out MotionLayer layer))
            {
                layer = new MotionLayer { instanceId = execution.instanceId };
                _layers.Add(execution.instanceId, layer);
                rebuild = true;
            }

            int animatorLayer = asset != null ? asset.animatorLayer : 0;
            AvatarMask mask = asset != null ? asset.mask : null;
            if (layer.animatorLayer != animatorLayer || layer.priority != execution.definition.priority || layer.mask != mask)
            {
                layer.animatorLayer = animatorLayer;
                layer.priority = execution.definition.priority;
                layer.mask = mask;
                rebuild = true;
            }
            if (layer.clip != clip)
            {
                DestroyClip(layer);
                layer.clip = clip;
                layer.playable = AnimationClipPlayable.Create(_graph, clip);
                layer.playable.SetSpeed(0d);
                layer.playable.SetDuration(double.MaxValue);
                layer.blendStartElapsed = execution.elapsed - execution.phaseElapsed;
                layer.blendDuration = asset != null ? asset.blendDuration : 0f;
                rebuild = true;
            }

            bool loop = execution.phase == AnimationPhase.Active
                && ((asset != null && asset.loop) || execution.definition.endMode == ActionEndMode.Held);
            double phaseTime = System.Math.Max(0d, execution.phaseElapsed);
            double clipTime = clip.length > 0f && loop ? phaseTime % clip.length : System.Math.Min(phaseTime, clip.length);
            layer.playable.SetTime(clipTime);
            layer.weight = layer.blendDuration > 0f
                ? Mathf.Clamp01((execution.elapsed - layer.blendStartElapsed) / layer.blendDuration) : 1f;

            if (rebuild)
            {
                RebuildMixer();
            }
            _mixer.SetInputWeight(layer.inputIndex, layer.weight);
        }

        public void EndExecution(long instanceId)
        {
            if (!_layers.TryGetValue(instanceId, out MotionLayer layer))
            {
                return;
            }
            DestroyClip(layer);
            _layers.Remove(instanceId);
            if (_layers.Count == 0)
            {
                ReleaseGraph();
            }
            else
            {
                RebuildMixer();
            }
        }

        private void Update()
        {
            if (_bridge.IsDirty)
            {
                ApplyParameters();
                _bridge.ClearDirty();
            }
            SyncToSoA();
        }

        private void EnsureGraph()
        {
            if (_graph.IsValid())
            {
                return;
            }
            _graph = PlayableGraph.Create("CharacterMotions");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _output = AnimationPlayableOutput.Create(_graph, "CharacterMotions", _animator);
            if (_animator.runtimeAnimatorController != null)
            {
                _baseController = AnimatorControllerPlayable.Create(_graph, _animator.runtimeAnimatorController);
                ApplyStoredParameters();
            }
            RebuildMixer();
            _graph.Play();
        }

        private void RebuildMixer()
        {
            AnimationLayerMixerPlayable previous = _mixer;
            if (previous.IsValid())
            {
                for (int i = 0; i < previous.GetInputCount(); i++)
                {
                    _graph.Disconnect(previous, i);
                }
            }

            _orderedLayers.Clear();
            foreach (MotionLayer layer in _layers.Values)
            {
                _orderedLayers.Add(layer);
            }
            _orderedLayers.Sort(CompareLayers);

            _mixer = AnimationLayerMixerPlayable.Create(_graph, _orderedLayers.Count + 1);
            if (_baseController.IsValid())
            {
                _graph.Connect(_baseController, 0, _mixer, 0);
                _mixer.SetInputWeight(0, 1f);
            }
            for (int i = 0; i < _orderedLayers.Count; i++)
            {
                MotionLayer layer = _orderedLayers[i];
                layer.inputIndex = i + 1;
                _graph.Connect(layer.playable, 0, _mixer, layer.inputIndex);
                _mixer.SetInputWeight(layer.inputIndex, layer.weight);
                if (layer.mask != null)
                {
                    _mixer.SetLayerMaskFromAvatarMask((uint)layer.inputIndex, layer.mask);
                }
            }
            _output.SetSourcePlayable(_mixer);
            if (previous.IsValid())
            {
                _graph.DestroyPlayable(previous);
            }
        }

        private static int CompareLayers(MotionLayer first, MotionLayer second)
        {
            int order = first.animatorLayer.CompareTo(second.animatorLayer);
            if (order != 0) { return order; }
            order = first.priority.CompareTo(second.priority);
            return order != 0 ? order : first.instanceId.CompareTo(second.instanceId);
        }

        private void DestroyClip(MotionLayer layer)
        {
            if (layer.inputIndex >= 0 && _mixer.IsValid() && layer.inputIndex < _mixer.GetInputCount())
            {
                _graph.Disconnect(_mixer, layer.inputIndex);
            }
            if (layer.playable.IsValid())
            {
                _graph.DestroyPlayable(layer.playable);
            }
            layer.playable = default;
            layer.inputIndex = -1;
        }

        private void ReleaseGraph()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
            _layers.Clear();
            _orderedLayers.Clear();
            _graph = default;
            _mixer = default;
            _baseController = default;
            _output = default;
            ApplyStoredParameters();
        }

        private void ApplyStoredParameters()
        {
            bool hasController = _baseController.IsValid();
            if (!hasController && (_animator == null || _animator.runtimeAnimatorController == null))
            {
                return;
            }
            foreach (KeyValuePair<int, float> pair in _bridge.Floats)
            {
                if (hasController) { _baseController.SetFloat(pair.Key, pair.Value); }
                else { _animator.SetFloat(pair.Key, pair.Value); }
            }
            foreach (KeyValuePair<int, bool> pair in _bridge.Bools)
            {
                if (hasController) { _baseController.SetBool(pair.Key, pair.Value); }
                else { _animator.SetBool(pair.Key, pair.Value); }
            }
            foreach (KeyValuePair<int, int> pair in _bridge.Ints)
            {
                if (hasController) { _baseController.SetInteger(pair.Key, pair.Value); }
                else { _animator.SetInteger(pair.Key, pair.Value); }
            }
        }

        private void ApplyParameters()
        {
            bool hasController = _baseController.IsValid();
            bool hasAnimatorController = _animator.runtimeAnimatorController != null;
            foreach (int hash in _bridge.DirtyFloats)
            {
                if (hasController) { _baseController.SetFloat(hash, _bridge.GetFloat(hash)); }
                else if (hasAnimatorController) { _animator.SetFloat(hash, _bridge.GetFloat(hash)); }
            }
            foreach (int hash in _bridge.DirtyBools)
            {
                if (hasController) { _baseController.SetBool(hash, _bridge.GetBool(hash)); }
                else if (hasAnimatorController) { _animator.SetBool(hash, _bridge.GetBool(hash)); }
            }
            foreach (int hash in _bridge.DirtyInts)
            {
                if (hasController) { _baseController.SetInteger(hash, _bridge.GetInt(hash)); }
                else if (hasAnimatorController) { _animator.SetInteger(hash, _bridge.GetInt(hash)); }
            }
            foreach (int trigger in _bridge.PendingTriggers)
            {
                if (hasController) { _baseController.SetTrigger(trigger); }
                else if (hasAnimatorController) { _animator.SetTrigger(trigger); }
            }
            _bridge.ConsumeAllTriggers();
        }

        private static AnimationClip SelectClip(AnimationPhase phase, MotionInfo motion)
        {
            switch (phase)
            {
                case AnimationPhase.Anticipation: return motion.preMotionClip;
                case AnimationPhase.Active: return motion.activeClip;
                case AnimationPhase.Recovery: return motion.recoveryClip;
                default: return null;
            }
        }

        private void SyncToSoA()
        {
            if (_isInitialized && GameManager.IsCharacterValid(_ownerHash))
            {
                GameManager.Data.GetAnimationState(_ownerHash) = _bridge.CurrentState;
            }
        }
    }
}
