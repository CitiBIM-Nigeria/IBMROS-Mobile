using System;
using System.Collections.Generic;
using SilverTau.RoomPlanUnity;
using UnityEngine;

namespace SilverTau.Sample
{
    [Serializable]
    public class IndoorModelValue
    {
        public CapturedRoom.Category category;
        public GameObject prefab;
    }
    
    [CreateAssetMenu(fileName = "Indoor Models Settings", menuName = "SilverTau/RoomPlan Unity Kit/Indoor Models Settings", order = 1)]
    public class IndoorModelsSettings : ScriptableObject
    {
        public List<IndoorModelValue> indoorModels = new List<IndoorModelValue>();
    }
}
