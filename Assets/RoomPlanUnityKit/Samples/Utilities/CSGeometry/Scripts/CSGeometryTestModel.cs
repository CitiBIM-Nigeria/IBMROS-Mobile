using System.Collections.Generic;
using UnityEngine;

namespace SilverTau.Utilities.Sample
{
    public class CSGeometryTestModel : MonoBehaviour
    {
        /// <summary>
        /// The container where the generated model will be added.
        /// </summary>
        public Transform container;
        
        /// <summary>
        /// The material that will be added to the object rendering.
        /// </summary>
        public Material material;
        
        /// <summary>
        /// The main target model.
        /// </summary>
        public GameObject targetObject => _targetObject;
        
        /// <summary>
        /// Objects that will interact with the target model.
        /// </summary>
        public List<GameObject> arrayObject => _arrayObject;
        
        private GameObject _targetObject;
        private List<GameObject> _arrayObject = new List<GameObject>();
        
        /// <summary>
        /// A function that is executed at startup and performs the action of creating test objects.
        /// </summary>
        private void Start()
        {
            _arrayObject.Clear();
            
            _targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            
            var meshFilter = _targetObject.GetComponent<MeshFilter>();
            var originalMesh = meshFilter.sharedMesh;
            var clonedMesh = new Mesh
            {
                name = "clone",
                vertices = originalMesh.vertices,
                triangles = originalMesh.triangles,
                normals = originalMesh.normals,
                uv = originalMesh.uv
            };

            meshFilter.mesh = clonedMesh;
            
            var meshRenderer = _targetObject.GetComponent<MeshRenderer>();
            meshRenderer.material = material;
            
            _targetObject.transform.parent = transform;
            _targetObject.transform.position = new Vector3(0, 0, 0);
            _targetObject.transform.localScale = new Vector3(10, 10, 0.2f);

            for (int i = 0; i < 10; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            
                var mf = go.GetComponent<MeshFilter>();
                var om = mf.sharedMesh;
                var cm = new Mesh
                {
                    name = "clone",
                    vertices = om.vertices,
                    triangles = om.triangles,
                    normals = om.normals,
                    uv = om.uv
                };

                mf.mesh = cm;
                
                var mr = go.GetComponent<MeshRenderer>();
                mr.material = material;
                
                go.transform.parent = container;
                go.transform.position = new Vector3(UnityEngine.Random.Range(-4, 4), UnityEngine.Random.Range(-4, 4), 0);
                go.transform.localScale = new Vector3(1, 1, 1);
                _arrayObject.Add(go);
            }
        }
    }
}
