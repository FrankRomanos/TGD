using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2.Integration
{
    /// <summary>
    /// 把 TestDriver 里的 Unit 接到 HexOccupancy（正式层）。
    /// 确保：放置一次；移动后把旧位置清掉，不会留下“幽灵占位”。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HexBoardTestDriver))]
    public sealed class UnitOccupancyBinder : MonoBehaviour
    {
        public HexOccupancyService occupancyService;
        public FootprintShape overrideFootprint;   // 可不设，默认单格
        public bool debugLog;

        HexBoardTestDriver _driver;
        HexOccupancy _occ;
        PlayerActorAdapter _actor;
        bool _placed;

        void Awake()
        {
            _driver = GetComponent<HexBoardTestDriver>();
            if (!occupancyService)
                occupancyService = GetComponentInParent<HexOccupancyService>(true);
            if (!occupancyService && _driver != null)
                occupancyService = _driver.GetComponentInParent<HexOccupancyService>(true);

            _occ = occupancyService ? occupancyService.Get() : null;

            if (_occ == null && _driver != null && _driver.authoring?.Layout != null)
                _occ = new HexOccupancy(_driver.authoring.Layout);

            _actor = new PlayerActorAdapter(_driver, overrideFootprint);
        }

        void OnEnable()
        {
            TryPlaceAtDriverStart();
        }

        void OnDisable()
        {
            TryRemove();
        }

        void TryPlaceAtDriverStart()
        {
            if (_occ == null && occupancyService)
                _occ = occupancyService.Get();
            if (_occ == null || !_driver || !_driver.IsReady || _driver.UnitRef == null) return;

            _actor.Anchor = _driver.UnitRef.Position;
            _actor.Facing = _driver.UnitRef.Facing;

            // 先清理同 ID 残留（容错）
            if (_placed) TryRemove();

            if (_occ.TryPlace(_actor, _actor.Anchor, _actor.Facing))
            {
                _placed = true;
                if (debugLog) Debug.Log($"[Occ] Place {_actor.Id} at {_actor.Anchor}", this);
            }
            else if (debugLog)
            {
                Debug.LogWarning($"[Occ] Failed to place {_actor.Id} at {_actor.Anchor}", this);
            }
        }

        public void MoveCommit(Hex newAnchor, Facing4 newFacing)
        {
            if (_occ == null && occupancyService)
                _occ = occupancyService.Get();
            if (_occ == null || _actor == null) return;
            if (!_placed) TryPlaceAtDriverStart();

            // 优先 Move；不支持 Move 就降级为 Remove+Place（按你的 API 名字替换）
            var prevFacing = _actor.Facing;
            _actor.Facing = newFacing;

            if (_occ.TryMove(_actor, newAnchor))
            {
                _actor.Anchor = newAnchor;
                _actor.Facing = newFacing;
                if (debugLog) Debug.Log($"[Occ] Move {_actor.Id} -> {newAnchor} facing={newFacing}", this);
            }
            else
            {
                _actor.Facing = prevFacing;
                TryRemove();
                _actor.Anchor = newAnchor;
                _actor.Facing = newFacing;
                if (_occ.TryPlace(_actor, newAnchor, newFacing))
                {
                    _placed = true;
                    if (debugLog) Debug.Log($"[Occ] RePlace {_actor.Id} at {newAnchor}", this);
                }
                else if (debugLog)
                {
                    Debug.LogWarning($"[Occ] Failed to RePlace {_actor.Id} at {newAnchor}", this);
                }
            }
        }
        void TryRemove()
        {
            if (_occ == null || !_placed || _actor == null) return;
            _occ.Remove(_actor);
            _placed = false;
            if (debugLog) Debug.Log($"[Occ] Remove {_actor.Id}", this);
        }

        public IGridActor Actor => _actor;
        public Hex CurrentAnchor => _actor != null ? _actor.Anchor : default;
        public HexOccupancy Occupancy => _occ;

        // —— 内部适配器：把 TestDriver 的 Unit 包装成 IGridActor ——
        sealed class PlayerActorAdapter : IGridActor
        {
            readonly HexBoardTestDriver _d;
            readonly FootprintShape _fp;

            public PlayerActorAdapter(HexBoardTestDriver d, FootprintShape overrideFp)
            {
                _d = d;
                _fp = overrideFp ? overrideFp : CreateSingle();
                if (d != null && d.UnitRef != null)
                {
                    Anchor = d.UnitRef.Position;
                    Facing = d.UnitRef.Facing;
                }
                else
                {
                    Anchor = Hex.Zero;
                    Facing = Facing4.PlusQ;
                }
            }
            public string Id => (_d != null && !string.IsNullOrEmpty(_d.unitId)) ? _d.unitId : "Player";
            public Hex Anchor { get; set; }
            public Facing4 Facing { get; set; }
            public FootprintShape Footprint => _fp;

            static FootprintShape CreateSingle()
            {
                var s = ScriptableObject.CreateInstance<FootprintShape>();
                s.name = "PlayerFootprint_Single_Runtime";
                s.offsets = new() { new L2(0, 0) };
                return s;
            }
        }
    }
}

