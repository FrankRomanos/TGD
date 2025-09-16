using UnityEngine;

public class ActionBusyUI : MonoBehaviour
{
    private void Start()
    {
        UnitAction.Instance.OnBusyChanged += UnitAction_OnBusyChanged;
        hide();
    }
    private void show()
    {
        gameObject.SetActive(true);
    }
    private void hide() 
    {
        gameObject.SetActive(false);
    }

    private void UnitAction_OnBusyChanged(object sender,bool isBusy)
    {
        if (isBusy)
        {
            show();
        }
        else
        {
            hide();
        }
    }
}
