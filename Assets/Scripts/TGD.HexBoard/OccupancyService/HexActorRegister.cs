using UnityEngine;

namespace TGD.HexBoard
{
    public sealed class HexActorRegister : MonoBehaviour
    {
        public HexOccupancyService occService;
        public HexBoardTestDriver driver;           // ֱ�Ӹ��������е� TestDriver
        public FootprintShape footprint;        // ������ 4 ���ռλ SO

        IGridActor _actor;

        void Start()
        {
            if (occService == null || driver == null || !driver.IsReady) return;
            _actor = new UnitGridAdapter(driver.UnitRef, footprint);
            // ����ǰ Unit ��λ�úͳ���ע�ᵽ����ռλ
            occService.Register(_actor, driver.UnitRef.Position, driver.UnitRef.Facing);
        }

        void OnDisable()
        {
            if (_actor != null) occService.Unregister(_actor);
            _actor = null;
        }
    }
}
