using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.AudioV2
{
    public enum BattleAudioEvent
    {
        ChainPopupOpen,
    }

    [Serializable]
    public sealed class BattleAudioEventConfig
    {
        [SerializeField]
        BattleAudioEvent _eventType = BattleAudioEvent.ChainPopupOpen;

        [SerializeField]
        AudioClip _clip;

        [SerializeField]
        [Range(0f, 1f)]
        float _volume = 1f;

        public BattleAudioEvent EventType => _eventType;

        public AudioClip Clip => _clip;

        public float Volume => Mathf.Clamp01(_volume);
    }

    public sealed class BattleAudioManager : MonoBehaviour
    {
        static BattleAudioManager _instance;

        [Header("UI Event Routing")]
        [SerializeField]
        AudioSource _uiAudioSource;

        [SerializeField]
        List<BattleAudioEventConfig> _uiEventConfigs = new List<BattleAudioEventConfig>();

        readonly Dictionary<BattleAudioEvent, BattleAudioEventConfig> _eventLookup =
            new Dictionary<BattleAudioEvent, BattleAudioEventConfig>();

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            RebuildCache();
        }

        void OnValidate()
        {
            if (_instance == null || _instance == this)
                RebuildCache();
        }

        public static void PlayEvent(BattleAudioEvent eventType)
        {
            if (_instance == null)
                return;

            _instance.PlayEventInternal(eventType);
        }

        void RebuildCache()
        {
            _eventLookup.Clear();
            if (_uiEventConfigs == null)
                return;

            for (int i = 0; i < _uiEventConfigs.Count; i++)
            {
                var config = _uiEventConfigs[i];
                if (config == null)
                    continue;

                _eventLookup[config.EventType] = config;
            }
        }

        void PlayEventInternal(BattleAudioEvent eventType)
        {
            if (!_eventLookup.TryGetValue(eventType, out var config) || config == null)
                return;

            if (config.Clip == null)
                return;

            var source = ResolveAudioSource();
            if (source == null)
                return;

            source.PlayOneShot(config.Clip, config.Volume);
        }

        AudioSource ResolveAudioSource()
        {
            if (_uiAudioSource != null)
                return _uiAudioSource;

            _uiAudioSource = GetComponent<AudioSource>();
            if (_uiAudioSource != null)
                return _uiAudioSource;

            _uiAudioSource = gameObject.AddComponent<AudioSource>();
            _uiAudioSource.playOnAwake = false;
            _uiAudioSource.loop = false;
            return _uiAudioSource;
        }
    }
}
