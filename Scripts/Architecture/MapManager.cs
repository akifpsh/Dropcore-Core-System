using Photon.Pun;
using UnityEngine;

/// <summary>
/// Handles map loading and management via Photon instantiation.
/// </summary>
public class MapManager : MonoBehaviourPun
{
    public GameObject GetCurrentMap() => currentMap;
    public static MapManager Instance;
    private GameObject currentMap;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void LoadMapMaster(string mapName)
    {
        StartCoroutine(LoadAndInit(mapName));
    }

    private System.Collections.IEnumerator LoadAndInit(string mapName)
    {
        if (currentMap != null)
            Destroy(currentMap);

        string path = $"Gameplay/Maps/MapPrefab/{mapName}";

        // Online ise Photon, offline testte normal Instantiate:
        if (Photon.Pun.PhotonNetwork.IsConnected)
            currentMap = Photon.Pun.PhotonNetwork.Instantiate(path, Vector3.zero, Quaternion.identity);
        else
            currentMap = Instantiate(Resources.Load<GameObject>(path), Vector3.zero, Quaternion.identity);

        // Collider/Bounds’ların oturması için 1 frame bekle
        yield return null;

        // Platform grafını kur
        var pm = PlatformManager.Instance ?? FindObjectOfType<PlatformManager>();
        pm?.InitializePlatforms();
    }

}
