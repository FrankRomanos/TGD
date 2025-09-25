using UnityEngine;
using UnityEngine.UI;
using TGD.Combat;
namespace TGD.UI
{
    public sealed class EndTurnButton : BaseTurnUiBehaviour
    {
        public Button button;
        Unit _activeUnit;

        protected override void Awake()
        {
            base.Awake();
            if (!button)
                button = GetComponent<Button>();
            if (button)
            {
                button.onClick.RemoveListener(EndTurn);
                button.onClick.AddListener(EndTurn);
            }
            UpdateState();
        }

        protected override void HandleTurnBegan(Unit u)
        {
            _activeUnit = u;
            UpdateState();
        }

        protected override void HandleTurnEnded(Unit u)
        {
            if (ReferenceEquals(_activeUnit, u))
                _activeUnit = null;
            UpdateState();
        }

        public void EndTurn()
        {
            if (_activeUnit != null)
                combat?.EndActiveTurn();
        }

        void UpdateState()
        {
            if (!button)
                return;
            bool interactable = _activeUnit != null;
            button.interactable = interactable;
        }
    }
}
