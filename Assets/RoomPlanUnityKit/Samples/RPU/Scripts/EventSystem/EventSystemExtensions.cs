using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SilverTau.Sample
{
    public static class EventSystemExtensions
    {
        public static bool IsPointerOverUI(this EventSystem eventSystem)
        {
            var eventDataCurrentPosition = new PointerEventData(eventSystem)
            {
                position = new Vector2(Input.mousePosition.x, Input.mousePosition.y)
            };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(eventDataCurrentPosition, results);
            return results.Count > 0;
        }
    }
}
