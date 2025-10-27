using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace TGD.AudioV2
{
    public enum BattleAudioEvent { ChainPopupOpen }

    [Serializable]
    public sealed class BattleAudioEventConfig
    {
        [SerializeField] BattleAudioEvent _eventType = BattleAudioEvent.ChainPopupOpen;
        [SerializeField] AudioClip _clip;
        [SerializeField, Range(0f, 1f)] float _volume = 1f;

        public BattleAudioEvent EventType => _eventType;
        public AudioClip Clip => _clip;
        public float Volume => Mathf.Clamp01(_volume);
    }

    [DefaultExecutionOrder(-5000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class BattleAudioManager : MonoBehaviour
    {
        static BattleAudioManager _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        [Header("UI Event Routing")]
        [SerializeField] AudioSource _uiAudioSource;
        [SerializeField] List<BattleAudioEventConfig> _uiEventConfigs = new();

        [Header("Mixer (optional)")]
        [SerializeField] AudioMixerGroup _uiMixerGroup;

        [Header("Lifecycle")]
        [SerializeField] bool _persistAcrossScenes = true;
        [SerializeField] bool _forceIgnoreRaycastLayer = true;

        readonly Dictionary<BattleAudioEvent, BattleAudioEventConfig> _eventLookup = new();

        int _originalLayer;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            _originalLayer = gameObject.layer;
            if (_forceIgnoreRaycastLayer)
            {
                int targetLayer = LayerMask.NameToLayer("Ignore Raycast");
                if (targetLayer >= 0)
                    gameObject.layer = targetLayer;
            }

            if (_persistAcrossScenes)
            {
                if (!transform.parent)
                {
                    DontDestroyOnLoad(gameObject);
                }
                else
                {
                    Debug.LogWarning("[Audio] BattleAudioManager is parented; skipping DontDestroyOnLoad to avoid detaching other UI components. Place it at the scene root or disable persistence.");
                }
            }

            RebuildCache();

            var src = ResolveAudioSource();
            if (_uiMixerGroup && src.outputAudioMixerGroup != _uiMixerGroup)
                src.outputAudioMixerGroup = _uiMixerGroup;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            if (_forceIgnoreRaycastLayer && gameObject.layer != _originalLayer)
                gameObject.layer = _originalLayer;
        }

        void OnValidate()
        {
            if (_instance == null || _instance == this)
                RebuildCache();
        }

        void RebuildCache()
        {
            _eventLookup.Clear();
            if (_uiEventConfigs == null) return;
            foreach (var c in _uiEventConfigs)
                if (c != null) _eventLookup[c.EventType] = c;
        }

        public static void PlayEvent(BattleAudioEvent evt)
        {
            if (_instance == null)
            {
                Debug.LogWarning($"[Audio] PlayEvent({evt}) called but manager not ready.");
                return;
            }

            _instance.PlayEventInternal(evt);
        }

        void PlayEventInternal(BattleAudioEvent evt)
        {
            if (!_eventLookup.TryGetValue(evt, out var cfg) || cfg?.Clip == null) return;

            var src = ResolveAudioSource();
            if (!src) return;
            src.PlayOneShot(cfg.Clip, cfg.Volume);
        }

        AudioSource ResolveAudioSource()
        {
            if (_uiAudioSource) return _uiAudioSource;

            _uiAudioSource = GetComponent<AudioSource>();
            if (!_uiAudioSource) _uiAudioSource = gameObject.AddComponent<AudioSource>();

            _uiAudioSource.playOnAwake = false;
            _uiAudioSource.loop = false;
            _uiAudioSource.spatialBlend = 0f;
            return _uiAudioSource;
        }
    }
}
