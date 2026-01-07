using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class Bullet : MonoBehaviourPun
{
    private WeaponData weaponData;
    public WeaponData WeaponData => weaponData;
    private WeaponController weaponController;

    private float bulletLifetime;
    private float lifeTimer;
    public float baseBulletSpeed = 10f;
    private float currentBulletSpeed;
    public int shooterActorNumber;

    // TRAIL
    [SerializeField] float trailBackOffset = 0.08f; 
    private Transform trailRoot;   
    private TrailRenderer[] trails;
    private float[] trailDefaultTimes;

    private Rigidbody2D rb;

    void Awake()
    {
        trailRoot = transform.Find("TrailBulletTrail"); 
        rb = GetComponent<Rigidbody2D>();
        weaponController = FindObjectOfType<WeaponController>();

       // Bullet ve çocuklarındaki tüm TrailRenderer'ları al
        trails = GetComponentsInChildren<TrailRenderer>(true);

        if (trails != null && trails.Length > 0)
        {
            trailDefaultTimes = new float[trails.Length];

            for (int i = 0; i < trails.Length; i++)
            {
                var t = trails[i];

                t.time = 0.05f;           // çok kısa kuyruk
                t.minVertexDistance = 0.01f;
                t.emitting = false;       // başta kapalı

                trailDefaultTimes[i] = t.time;
            }
        }

        if (rb)
        {
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.freezeRotation = true;
        }
    }

    private void Start()
    {
        GameObject poolParent = GameObject.Find("BulletPoolParent");
        if (poolParent != null)
            transform.SetParent(poolParent.transform);
    }

    private void OnEnable()
    {
        if (GameManager.Instance != null)
        {
            float bulletSpeedEffect = GameManager.Instance.GetMapValue("Effect_BulletSpeed", 1f);
            currentBulletSpeed = baseBulletSpeed * bulletSpeedEffect;
        }

        if (trails != null)
            foreach (var t in trails) { t.Clear(); t.emitting = true; }

        if (rb != null)
        {
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.freezeRotation = true; 
        }
    }

    public void Initialize(int actorNumber, Vector2 direction, float speed, float lifetime, string weaponName)
    {
        shooterActorNumber = actorNumber;
        bulletLifetime = lifetime;
        lifeTimer = lifetime;
        weaponData = WeaponManager.Instance.GetWeapon(weaponName);

        gameObject.SetActive(true);

       Vector2 nd = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
        rb.velocity = nd * speed;
        transform.right = nd; 

        if (trails != null)
        foreach (var t in trails)
        {
            t.Clear();
            t.emitting = true;
        }
    }

    void Update()
    {
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
            ReturnToPool();
    }

    private void FixedUpdate()
    {
        if (rb == null || trailRoot == null) return;
        Vector2 v = rb.velocity;
        if (v.sqrMagnitude < 1e-6f) return;

        trailRoot.position = (Vector2)transform.position - v.normalized * trailBackOffset;
    }

   void OnCollisionEnter2D(Collision2D collision)
    {
        // Sadece Master Client hesaplar
        if (!PhotonNetwork.IsMasterClient)
        {
            ReturnToPool();
            return;
        }

        if (collision.gameObject.CompareTag("Player"))
        {
            var targetPC = collision.gameObject.GetComponent<PlayerController>();
            var targetPV = collision.gameObject.GetComponent<PhotonView>();

            int fallenActor = targetPC != null
                ? targetPC.ActorNumber
                : (targetPV != null ? targetPV.Owner.ActorNumber : -1);

            // Kendimizi vurmayalım
            if (fallenActor == shooterActorNumber)
            {
                return;
            }

            int shooterActor = shooterActorNumber;
            float hitTime = Time.time;
            float force = weaponData.knockbackForce;

            // OSOK Modu kontrolü
            if (GameManager.Instance != null &&
                GameManager.Instance.activeModeData.modeName == "OSOK" &&
                weaponData.weaponType == WeaponType.Sniper)
            {
                force *= 4f;
            }

            // Mermi duvara çarpıp sekse bile burnu hala ileri bakıyordur.
            Vector2 knockDir = transform.right; 
            
            // Y eksenini sıfırla (Havaya uçmayı engellemek için)
            knockDir.y = 0f; 
            knockDir.Normalize();

            // Eğer mermi çok dik geldiyse ve sıfırlanınca yön kaybolduysa, pozisyona göre manuel hesapla
            if (knockDir.sqrMagnitude < 0.01f)
            {
                knockDir = collision.transform.position.x > transform.position.x ? Vector2.right : Vector2.left;
            }

            object[] content = new object[]
            {
                fallenActor, shooterActor, hitTime, force, knockDir.x, knockDir.y
            };

            NetworkEventManager.RaiseEvent(
                EventCodes.PlayerHit,
                content,
                ReceiverGroup.All
            );
        }

        // Çarptığı an yok olsun
        ReturnToPool();
    }
    
    private void ReturnToPool()
    {
        if (rb != null)
            rb.velocity = Vector2.zero;

        if (trails != null)
        {
            foreach (var t in trails)
            {
                t.emitting = false;
                t.Clear();      // eski kuyruk tamamen silinsin
            }
        }

        gameObject.SetActive(false);
        BulletPool.Instance.ReturnBullet(gameObject);
    }
}
