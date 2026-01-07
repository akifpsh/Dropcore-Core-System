using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;
using ExitGames.Client.Photon;

public class WeaponManager : MonoBehaviourPunCallbacks
{
    public static WeaponManager Instance { get; private set; }

    [SerializeField]
    private List<WeaponData> weaponList;

    private Dictionary<string, WeaponData> weaponDictionary;

    private List<string> shuffledWeaponList;


    [SerializeField]
    private List<GameObject> weaponPrefabs;

    public List<WeaponData> GetAllWeapons()
    {
        return weaponList;
    }

    private Dictionary<WeaponType, GameObject> weaponPrefabDictionary;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        weaponPrefabDictionary = new Dictionary<WeaponType, GameObject>();
        foreach (GameObject prefab in weaponPrefabs)
        {
            WeaponPickup pickup = prefab.GetComponent<WeaponPickup>();
            if (pickup != null && pickup.weaponData != null)
            {
                weaponPrefabDictionary[pickup.weaponData.weaponType] = prefab;
            }
        }

        weaponDictionary = new Dictionary<string, WeaponData>();
        foreach (WeaponData weapon in weaponList)
        {
            weaponDictionary.Add(weapon.weaponName.ToLower(), weapon);
        }

        InitializeWeaponList();
    }

    private void InitializeWeaponList()
    {
        shuffledWeaponList = new List<string> { "rose", "infinus", "spider", "jedi", "khaos" };
        ShuffleAndSyncWeaponList();
    }

    public void ShuffleAndSyncWeaponList()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        System.Random rng = new System.Random();
        int n = shuffledWeaponList.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            (shuffledWeaponList[k], shuffledWeaponList[n])
              = (shuffledWeaponList[n], shuffledWeaponList[k]);
        }

        var props = new Hashtable
        {
            { "shuffledWeaponList", string.Join(",", shuffledWeaponList) }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);
        if (propertiesThatChanged.ContainsKey("shuffledWeaponList"))
        {
            string csv = (string)propertiesThatChanged["shuffledWeaponList"];
            shuffledWeaponList = new List<string>(csv.Split(','));
        }
    }

    public int GetShuffledWeaponListCount()
    {
        return shuffledWeaponList.Count;
    }

    public WeaponData GetWeapon(string weaponName)
    {
        if (Instance.weaponDictionary.TryGetValue(weaponName.ToLower(), out WeaponData weaponData))
            return weaponData;
        else
            return null;
    }

    public WeaponData GetRandomWeapon()
    {
        if (shuffledWeaponList.Count == 0) return null;

        // **Her do�u�ta tamamen rastgele bir silah se�**
        int randomIndex = Random.Range(0, shuffledWeaponList.Count);
        string selectedWeapon = shuffledWeaponList[randomIndex];
        return GetWeapon(selectedWeapon);
    }

    public WeaponData GetRandomSniper()
    {
        List<WeaponData> snipers = weaponList.FindAll(w => w.weaponType == WeaponType.Sniper);

        if (snipers.Count == 0) return null;

        int randomIndex = Random.Range(0, snipers.Count);
        return snipers[randomIndex];
    }

    public static WeaponData GetWeaponByLevel(int level)
    {
        if (Instance == null || Instance.shuffledWeaponList == null ||
            level >= Instance.shuffledWeaponList.Count) return null;

        string weaponName = Instance.shuffledWeaponList[level];
        return Instance.GetWeapon(weaponName);
    }

    public GameObject GetPrefabForWeaponType(WeaponType weaponType)
    {
        string prefabPath = $"Prefabs/WeaponPrefabs/{weaponType}";
        GameObject prefab = Resources.Load<GameObject>(prefabPath);
        return prefab;
    }

    public static Weapon CreateWeapon(WeaponInstance Instance, Transform firePoint, Rigidbody2D playerRb, BulletPool bulletPool, PlayerAim playerAim)
    {
        if (Instance == null || Instance.weaponData == null) return null;
        switch (Instance.weaponData.weaponType)
        {
            case WeaponType.Pistol:
                return new Pistol(Instance, firePoint, playerRb, bulletPool, playerAim);
            case WeaponType.SMG:
                return new SMG(Instance, firePoint, playerRb, bulletPool, playerAim);
            case WeaponType.AR:
                return new AR(Instance, firePoint, playerRb, bulletPool, playerAim);
            case WeaponType.Shotgun:
                return new Shotgun(Instance, firePoint, playerRb, bulletPool, playerAim);
            case WeaponType.Minigun:
                return new Minigun(Instance, firePoint, playerRb, bulletPool, playerAim);
            case WeaponType.Sniper:
                return new Sniper(Instance, firePoint, playerRb, bulletPool, playerAim);
            default:
                return null;
        }
    }

}
