using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewMap", menuName = "Map Data")]
public class MapData : ScriptableObject
{
    public string mapName;
    public Sprite mapImage;
    public string mapDescription;
    public List<MapFeature> mapFeatures;
    public GameObject mapPrefab;

    [System.Serializable]
    public class MapFeature
    {
        public string key;
        public float value;
    }
}
