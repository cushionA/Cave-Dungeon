using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// A named AI configuration preset. Can be system-defined or user-created.
    /// </summary>
    public struct AIPreset
    {
        public string presetName;
        public CompanionAIConfig config;
        public bool isSystemPreset;
    }

    /// <summary>
    /// Manages system and custom AI presets for the companion.
    /// System presets are read-only; custom presets can be saved, loaded, and deleted.
    /// </summary>
    public class PresetManager
    {
        private const int k_MaxCustomPresets = 20;

        private List<AIPreset> _systemPresets;
        private List<AIPreset> _customPresets;

        public IReadOnlyList<AIPreset> SystemPresets => _systemPresets;
        public IReadOnlyList<AIPreset> CustomPresets => _customPresets;
        public int CustomPresetCount => _customPresets.Count;

        public PresetManager()
        {
            _systemPresets = new List<AIPreset>();
            _customPresets = new List<AIPreset>();
        }

        /// <summary>
        /// Adds a system-defined preset (cannot be deleted by the user).
        /// </summary>
        public void AddSystemPreset(AIPreset preset)
        {
            preset.isSystemPreset = true;
            _systemPresets.Add(preset);
        }

        /// <summary>
        /// Saves a user-created preset. Returns false if the custom preset limit is reached.
        /// </summary>
        public bool SaveCustomPreset(string name, CompanionAIConfig config)
        {
            if (_customPresets.Count >= k_MaxCustomPresets)
            {
                return false;
            }

            _customPresets.Add(new AIPreset
            {
                presetName = name,
                config = config,
                isSystemPreset = false
            });
            return true;
        }

        /// <summary>
        /// Loads a preset config by index. Returns null if the index is out of range.
        /// </summary>
        public CompanionAIConfig? LoadPreset(int index, bool isSystem)
        {
            List<AIPreset> list = isSystem ? _systemPresets : _customPresets;
            if (index < 0 || index >= list.Count)
            {
                return null;
            }

            return list[index].config;
        }

        /// <summary>
        /// Deletes a custom preset by index. Returns false if the index is invalid.
        /// </summary>
        public bool DeleteCustomPreset(int index)
        {
            if (index < 0 || index >= _customPresets.Count)
            {
                return false;
            }

            _customPresets.RemoveAt(index);
            return true;
        }
    }
}
