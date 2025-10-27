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

    [DefaultExecutionOrder(-5000)]                // 让它比大多数系统更早初始化
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class BattleAudioManager : MonoBehaviour
    {
        static BattleAudioManager _instance;

        // ★ 关键：在“子系统注册”阶段重置静态（即使 Domain Reload 关闭也会调用）
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        [Header("UI Event Routing")]
        [SerializeField] AudioSource _uiAudioSource;                   // 可留空，自动抓取/创建
        [SerializeField] List<BattleAudioEventConfig> _uiEventConfigs = new();

        [Header("Mixer (optional)")]
        [SerializeField] AudioMixerGroup _uiMixerGroup;                // 可留空

        readonly Dictionary<BattleAudioEvent, BattleAudioEventConfig> _eventLookup = new();

        void Awake()
        {
            // 单例稳态
            if (_instance != null && _instance != this)
            {
                // 若已经有常驻实例，当前这个直接销毁并退出 —— 避免双实例干扰其它系统
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // 放到一个不会拦 UI 射线的层（保险）
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

            DontDestroyOnLoad(gameObject);
            RebuildCache();

            // AudioSource就绪 & 路由到UI组（如有）
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
            // 永不抛异常，最多打一条 Warning，避免把 UI 流程中断
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
            _uiAudioSource.spatialBlend = 0f;      // UI音效→纯2D
            return _uiAudioSource;
        }
    }
}
