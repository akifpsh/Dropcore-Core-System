using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

public class BulletPool : MonoBehaviourPun
{
    public static BulletPool Instance { get; private set; }

    public GameObject bulletPrefab;
    private List<GameObject> pool = new List<GameObject>();
    private Transform parentBulletPool;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        GameObject parentObject = new GameObject("BulletPoolParent");
        parentBulletPool = parentObject.transform;
    }

    public GameObject GetBullet(Vector3 position, Quaternion rotation)
    {
        // Havuzda hazýr mermi var mý kontrol et
        foreach (GameObject bullet in pool)
        {
            if (!bullet.activeInHierarchy)
            {
                bullet.transform.position = position;
                bullet.transform.rotation = rotation;
                bullet.SetActive(true);
                return bullet;
            }
        }

        // Yoksa yeni bir mermi instantiate et (network baðýmsýz)
        GameObject newBullet = Instantiate(bulletPrefab, position, rotation);
        newBullet.transform.SetParent(parentBulletPool);
        pool.Add(newBullet);
        return newBullet;
    }

    public void ReturnBullet(GameObject bullet)
    {
        bullet.SetActive(false);
    }

    public void ClearAllBullets()
    {
        foreach (GameObject bullet in pool)
        {
            if (bullet != null)
            {
                bullet.SetActive(false);
            }
        }
        pool.Clear();
    }
}
