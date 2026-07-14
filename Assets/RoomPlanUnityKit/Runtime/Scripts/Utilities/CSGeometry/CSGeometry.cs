using System;
using SilverTau.CSGeometry;
using UnityEngine;

namespace SilverTau.Utilities
{
    public static class CSGeometry
    {
        #region CSGeometry

        /// <summary>
        /// Additive is a function that combines objects into a single object.
        /// </summary>
        /// <param name="targetObject">Target, the main object with which changes will take place.</param>
        /// <param name="objects">Objects that will interact with the targeted object.</param>
        public static void Additive(GameObject targetObject, GameObject[] objects)
        {
            var cSGeometryWorker = new CSGeometryWorker();
            cSGeometryWorker.Additive(targetObject, objects);
        }
        
        /// <summary>
        /// Subtractive is a function that subtracts objects from each other.
        /// </summary>
        /// <param name="targetObject">Target, the main object with which changes will take place.</param>
        /// <param name="objects">Objects that will interact with the targeted object.</param>
        public static void Subtractive(GameObject targetObject, GameObject[] objects)
        {
            var cSGeometryWorker = new CSGeometryWorker();
            cSGeometryWorker.Subtractive(targetObject, objects);
        }
        
        /// <summary>
        /// Intersect is a function that creates a common part for objects.
        /// </summary>
        /// <param name="targetObject">Target, the main object with which changes will take place.</param>
        /// <param name="objects">Objects that will interact with the targeted object.</param>
        public static void Intersect(GameObject targetObject, GameObject[] objects)
        {
            var cSGeometryWorker = new CSGeometryWorker();
            cSGeometryWorker.Intersect(targetObject, objects);
        }
        
        /// <summary>
        /// DeIntersect is a function that removes the common part for objects.
        /// </summary>
        /// <param name="targetObject">Target, the main object with which changes will take place.</param>
        /// <param name="objects">Objects that will interact with the targeted object.</param>
        public static void DeIntersect(GameObject targetObject, GameObject[] objects)
        {
            var cSGeometryWorker = new CSGeometryWorker();
            cSGeometryWorker.DeIntersect(targetObject, objects);
        }
        
        #endregion
    }
}
