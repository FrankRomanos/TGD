using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
[RequireComponent(typeof(AspectRatioFitter))]
[ExecuteAlways]
public class IconCoverAutoAspect : MonoBehaviour
{
    Image _img;
    AspectRatioFitter _fitter;

    void Awake()
    {
        _img = GetComponent<Image>();
        _fitter = GetComponent<AspectRatioFitter>();
        _fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        Apply();
    }

    void OnEnable() => Apply();
#if UNITY_EDITOR
    void OnValidate() => Apply();
    void Update() { if (!Application.isPlaying) Apply(); }
#endif

    public void Apply()
    {
        if (!_img || !_img.sprite) return;
        var r = _img.sprite.rect;
        if (r.height > 0f) _fitter.aspectRatio = r.width / r.height;
    }
}

