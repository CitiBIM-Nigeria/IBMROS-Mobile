using UnityEngine;

public class FurnitureColorLoader : MonoBehaviour
{
    [Header("Drag the mesh_only.glb object here")]
    public GameObject furnitureObject;

    [Header("Drag textures from Project panel here")]
    public Texture2D baseColorTexture;
    public Texture2D normalMapTexture;

    void Start()
    {
        ApplyTextures();
    }

    public void ApplyTextures()
    {
        if (furnitureObject == null)
        {
            Debug.LogError("FurnitureColorLoader: furnitureObject is not assigned.");
            return;
        }

        var renderers = furnitureObject.GetComponentsInChildren<Renderer>();

        foreach (var rend in renderers)
        {
            // Clone the material so other objects are not affected
            Material mat = new Material(rend.sharedMaterial);

            if (baseColorTexture != null)
                mat.mainTexture = baseColorTexture;

            if (normalMapTexture != null)
            {
                mat.SetTexture("_BumpMap", normalMapTexture);
                mat.EnableKeyword("_NORMALMAP");
            }

            rend.material = mat;
        }

        Debug.Log($"Applied textures to {renderers.Length} renderer(s) on {furnitureObject.name}");
    }
}