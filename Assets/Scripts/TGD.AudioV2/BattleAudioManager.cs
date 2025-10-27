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

    [DefaultExecutionOrder(-5000)]                // �����ȴ����ϵͳ�����ʼ��
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class BattleAudioManager : MonoBehaviour
    {
        static BattleAudioManager _instance;

        // �� �ؼ����ڡ���ϵͳע�ᡱ�׶����þ�̬����ʹ Domain Reload �ر�Ҳ����ã�
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        [Header("UI Event Routing")]
        [SerializeField] AudioSource _uiAudioSource;                   // �����գ��Զ�ץȡ/����
        [SerializeField] List<BattleAudioEventConfig> _uiEventConfigs = new();

        [Header("Mixer (optional)")]
        [SerializeField] AudioMixerGroup _uiMixerGroup;                // ������

        readonly Dictionary<BattleAudioEvent, BattleAudioEventConfig> _eventLookup = new();

        void Awake()
        {
            // ������̬
            if (_instance != null && _instance != this)
            {
                // ���Ѿ��г�פʵ������ǰ���ֱ�����ٲ��˳� ���� ����˫ʵ����������ϵͳ
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // �ŵ�һ�������� UI ���ߵĲ㣨���գ�
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

            DontDestroyOnLoad(gameObject);
            RebuildCache();

            // AudioSource���� & ·�ɵ�UI�飨���У�
            var src = ResolveAudioSource();
            if (_uiMixerGroup && src.outputAudioMixerGroup != _uiMixerGroup)
                src.outputAudioMixerGroup = _uiMixerGroup;
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
            // �������쳣������һ�� Warning������� UI �����ж�
            if (_instance == null) { Debug.LogWarning($"[Audio] PlayEvent({evt}) called but manager not ready."); return; }
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
            _uiAudioSource.spatialBlend = 0f;      // UI��Ч����2D
            return _uiAudioSource;
        }
    }
}
