using UnityEngine;

/// <summary>
/// Dynamically creates a soft radial gradient shadow quad beneath the parent object.
/// This grounds the furniture without requiring expensive real-time shadows or external PNGs.
/// </summary>
public class BlobShadow : MonoBehaviour
{
    private GameObject _shadowQuad;
    private Material _shadowMaterial;
    private Texture2D _shadowTexture;

    [SerializeField] private float shadowSize = 1.5f;
    [SerializeField] private float shadowOpacity = 0.6f;
    [SerializeField] private float floorOffset = 0.02f; // Slightly above floor to prevent Z-fighting

    void Start()
    {
        CreateShadow();
    }

    private void CreateShadow()
    {
        // 1. Create a radial gradient texture programmatically
        _shadowTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        _shadowTexture.wrapMode = TextureWrapMode.Clamp;
        
        Vector2 center = new Vector2(32, 32);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float distance = Vector2.Distance(center, new Vector2(x, y));
                // Soft falloff from center (radius 32)
                float alpha = Mathf.Clamp01(1f - (distance / 32f));
                // Square it for a smoother curve
                alpha *= alpha;
                _shadowTexture.SetPixel(x, y, new Color(0, 0, 0, alpha * shadowOpacity));
            }
        }
        _shadowTexture.Apply();

        // 2. Load the custom BlobShadow shader from Resources to prevent build stripping
        Shader shadowShader = Resources.Load<Shader>("Shaders/BlobShadow");
        if (shadowShader == null)
        {
            Debug.LogError("[BlobShadow] Could not find BlobShadow shader in Resources!");
            return;
        }

        _shadowMaterial = new Material(shadowShader);
        _shadowMaterial.SetTexture("_BaseMap", _shadowTexture);
        _shadowMaterial.SetColor("_BaseColor", Color.white);

        // 3. Create the Quad and place it
        _shadowQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _shadowQuad.name = "DynamicBlobShadow";
        
        // Remove the collider so it doesn't interfere with raycasts
        Destroy(_shadowQuad.GetComponent<Collider>());

        _shadowQuad.transform.SetParent(this.transform);
        
        // Calculate the bounding box of the furniture to size the shadow appropriately
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0 && renderers[0].gameObject != _shadowQuad)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                if (r.gameObject != _shadowQuad) bounds.Encapsulate(r.bounds);
            }

            float maxDim = Mathf.Max(bounds.size.x, bounds.size.z);
            shadowSize = maxDim * 1.2f; // Slightly larger than the object
            
            // Place shadow exactly at the lowest point of the object, plus offset
            float lowestY = bounds.min.y - this.transform.position.y;
            _shadowQuad.transform.localPosition = new Vector3(0, lowestY + floorOffset, 0);
        }
        else
        {
            _shadowQuad.transform.localPosition = new Vector3(0, floorOffset, 0);
        }

        // Lay it flat on the ground
        _shadowQuad.transform.localRotation = Quaternion.Euler(90, 0, 0);
        _shadowQuad.transform.localScale = new Vector3(shadowSize, shadowSize, 1f);

        _shadowQuad.GetComponent<Renderer>().material = _shadowMaterial;
    }

    void OnDestroy()
    {
        if (_shadowQuad != null) Destroy(_shadowQuad);
        if (_shadowMaterial != null) Destroy(_shadowMaterial);
        if (_shadowTexture != null) Destroy(_shadowTexture);
    }
}
