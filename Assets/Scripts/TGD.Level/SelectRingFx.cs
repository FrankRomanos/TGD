using UnityEngine;

public class SelectRingFX : MonoBehaviour
{
    [Header("Glow")]
    public Renderer targetRenderer;
    public Color glowColor = new(1f, 0.85f, 0.1f);
    public float minIntensity = 1.2f;
    public float maxIntensity = 3.0f;
    public float pulseSpeed = 2.0f;

    [Header("Motion")]
    public bool rotate = true;
    public float rotationSpeed = 40f;
    public bool popOnEnable = true;
    public float popScale = 1.12f;
    public float popTime = 0.12f;

    Material mat;
    MaterialPropertyBlock mpb;
    static readonly int ID_Emiss = Shader.PropertyToID("_EmissionColor");
    static readonly int ID_Base = Shader.PropertyToID("_BaseColor");
    static readonly int ID_Color = Shader.PropertyToID("_Color");
    static readonly int ID_Tint = Shader.PropertyToID("_TintColor");
    static readonly int ID_Energy = Shader.PropertyToID("_EmissionStrength");

    // —— 关键：以“外部缩放”为基准做弹出，若外部改了，自动跟随 —— //
    Vector3 _baseScale;
    Vector3 _lastAppliedScale;
    Coroutine _popCo;

    void Awake()
    {
        if (!targetRenderer) targetRenderer = GetComponentInChildren<Renderer>(true);
        if (targetRenderer) mat = targetRenderer.material; // 独立材质
        mpb = new MaterialPropertyBlock();
        if (mat && mat.HasProperty(ID_Emiss)) mat.EnableKeyword("_EMISSION");
    }

    void OnEnable()
    {
        // 以“当前”大小为基准
        _baseScale = transform.localScale;
        _lastAppliedScale = _baseScale;

        if (popOnEnable)
        {
            if (_popCo != null) StopCoroutine(_popCo);
            _popCo = StartCoroutine(Pop());
        }
    }

    System.Collections.IEnumerator Pop()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, popTime);

            // 若外部（HexSelectVisual 等）改变了缩放，更新基准
            if (!Approximately(transform.localScale, _lastAppliedScale))
            {
                _baseScale = transform.localScale;
            }

            float s = Mathf.Lerp(1f, popScale, t);
            var newScale = _baseScale * s;
            transform.localScale = newScale;
            _lastAppliedScale = newScale;
            yield return null;
        }

        // 动画结束：恢复到“当前外部基准”，不强制还原到 (1,1,1)
        transform.localScale = _baseScale;
        _lastAppliedScale = _baseScale;
    }

    void Update()
    {
        float k = 0.5f * (1f + Mathf.Sin(Time.time * Mathf.PI * 2f * pulseSpeed));
        float intensity = Mathf.Lerp(minIntensity, maxIntensity, k);
        Color ec = glowColor * intensity; // HDR

        if (mat)
        {
            if (mat.HasProperty(ID_Emiss)) mat.SetColor(ID_Emiss, ec);
            if (mat.HasProperty(ID_Energy)) mat.SetFloat(ID_Energy, intensity);
            if (mat.HasProperty(ID_Base)) mat.SetColor(ID_Base, ec);
            if (mat.HasProperty(ID_Color)) mat.SetColor(ID_Color, ec);
            if (mat.HasProperty(ID_Tint)) mat.SetColor(ID_Tint, ec);
        }
        else if (targetRenderer)
        {
            targetRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(ID_Emiss, ec);
            targetRenderer.SetPropertyBlock(mpb);
        }

        if (rotate) transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.Self);
    }

    static bool Approximately(in Vector3 a, in Vector3 b)
        => Mathf.Abs(a.x - b.x) < 1e-4f && Mathf.Abs(a.y - b.y) < 1e-4f && Mathf.Abs(a.z - b.z) < 1e-4f;
}
