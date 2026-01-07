using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Controls player movement, jump, gravity, collisions, and knockback effects.
/// </summary>
public class PlayerController : MonoBehaviourPun
{
    // ===================== REFS & CORE =====================
    [Header("Core Refs")]
    public Rigidbody2D playerRb;
    public Collider2D currentPlatform;

    private PlayerData playerData;
    private AnimationManager animationManager;
    private PlayerAim playerAim;
    private ExternalForceAccumulator externalForceAccumulator;
    private PlayerNetworkSync sync;

    // ===================== NETWORK / STATE =====================
    public PlayerData PlayerData => playerData;
    public int ActorNumber { get; private set; }
    public bool IsBot { get; private set; }

    [Tooltip("Zemine temas?")]
    public bool isGrounded = false;
    [Tooltip("Respawn animasyonu/süreci sırasında kontrol kilidi.")]
    public bool isRespawning = false;

    public Vector2 inputVector;
    private string _lastAnimState = "Idle";

    // ===================== LEGACY (GEÇİCİ) =====================
    [Header("Legacy (Geçiş Sürecinde)")]
    [Tooltip("Artık kullanılmıyor; hareket tuning yukarıdaki yeni alanlardan geliyor.")]
    public static float baseMoveSpeed = 6f;
    private float currentSpeed;

    [Tooltip("Artık AddForce kullanılmadığı için 'v0' olarak hesaplanıp _jumpV0 kullanılıyor.")]
    public static float baseJumpForce = 18f;
    private float currentJumpForce;

    [Tooltip("Gravity artık 1 tabanda; Awake’te Physics2D.gravity ile yönetiyoruz.")]
    public static float baseGravityScale = 4f;
    private float gravityScale;

    // ===================== MOVEMENT (RUN) =====================
    [Header("Movement (Run)")]
    [Tooltip("Azami yatay hız (u/s). Öneri: 7.0–7.5 aralığı.")]
    [SerializeField] float maxRunSpeed = 2f;  // önce 7.4’tü; “bir tık yavaş” için 7.2

    [Tooltip("Zeminde ivmelenme (u/s²). Ne kadar hızlı hızlanır.")]
    [SerializeField] float groundAccel  = 45f;

    [Tooltip("Zeminde tuşu bırakınca yavaşlama (u/s²). Ne kadar hızlı durur.")]
    [SerializeField] float groundDecel  = 70f;

    [Tooltip("Zeminde ters yöne basınca ekstra fren (u/s²).")]
    [SerializeField] float turnDecel    = 45f;

    [Space(6)]
    [Tooltip("Havada yatay kontrol gücü (u/s²). Genelde groundAccel × 0.8.")]
    [SerializeField] float airAccel     = 30f;

    [Tooltip("Havada hız koruma / yavaşlama (u/s²). Genelde groundDecel × ~0.9.")]
    [SerializeField] float airDecel     = 40f;

    [Tooltip("Maksimum düşüş hızı (u/s). Terminal hız.")]
    [SerializeField] float maxFallSpeed = 26f;

    [Header("External Forces (Clamp)")]
    [SerializeField] float externalGroundMul     = 0.65f; // yerde itişleri azalt
    [SerializeField] float externalAirMul        = 1.00f; // havada tam uygula
    [SerializeField] float externalMaxPerFrameX  = 12.0f; // bu framedeki en fazla deltaV-x
    [SerializeField] float externalMaxPerFrameY  = 18.0f; // bu framedeki en fazla deltaV-y
    [SerializeField] float externalSpeedBonusCap = 12.0f; // koşu hız üstüne izinli ekstra

    [Header("Knockback Control (No Stun)")]
    [SerializeField] float kbControlStart = 1.5f;                 // bu seviyeden sonra kontrol azalmaya başlar
    [SerializeField] float kbControlFull  = 6.0f;                 // bu seviyede kontrol en düşükte olur
    [SerializeField, Range(0f,1f)] float kbMinControlFactor = 0.35f; // knockback altında minimum kontrol yüzdesi
    [SerializeField, Range(0f,1f)] float kbMaxOpposeRatio   = 0.40f; // knockback'a karşı yürüyüşün max etkisi (oran)

    [SerializeField] LayerMask groundMask;          // Platform katman(lar)ı
    [SerializeField] Vector2   feetOffset = new(0f, -0.48f);
    [SerializeField] Vector2 feetBox = new(0.26f, 0.06f);
    [SerializeField] float     groundCastWidth = 0.24f;
    [SerializeField] float     groundCastHeight = 0.08f;
    [SerializeField] float     groundCastDistance = 0.06f;

    CapsuleCollider2D _col; // root’taki ana collider

    [SerializeField] Animator animator;
    [SerializeField] float moveThreshold = 0.10f;
    float _speedXSmoothed;
    private string lastStateName;

    // ===================== JUMP & GRAVITY =====================
    [Header("🪜 Jump & Gravity")]
    [Tooltip("1. zıplamanın hedef yüksekliği (u).")]
    [SerializeField] float desiredJumpHeight = 4.2f;

    [Tooltip("Tepeye çıkış süresi (s). 0.35–0.45 aralığı iyi.")]
    [SerializeField] float timeToApex       = 0.40f;

    [Tooltip("2. zıplamada dikey hız hedefi (u/s). Roketlemeyi engeller.")]
    [SerializeField] float secondJumpVyTarget = 17.0f;

    [Space(6)]
    [Tooltip("Down/S basılıyken geçici yerçekimi çarpanı (fast-fall).")]
    [SerializeField] float fastFallMultiplier = 1.8f;

    // ===================== JUMP-CUT (OPSİYONEL, HAFİF) =====================
    [Header("Jump-Cut (Kısa Basma Etkisi)")]
    [SerializeField] float secondJumpExtraHeight = 2.2f;

    [Tooltip("Erken bırakırsan zıplama çıkışını yumuşak kısalt.")]
    [SerializeField] bool  jumpCutEnabled      = true;

    [Tooltip("Jump-cut sırasında geçici gravity çarpanı.")]
    [SerializeField] float jumpCutGravityMult  = 2.0f;

    [Tooltip("Zıpladıktan sonra cut için geçerli pencere (s). Örn: 0.12s")]
    [SerializeField] float jumpCutWindow       = 0.12f;

    [Tooltip("Cut olsa bile en az bu kadar yukarı hız kalsın (u/s).")]
    [SerializeField] float minUpVelAfterCut    = 6.0f;


    // ===================== RUNTIME (PRIVATE) =====================
    [Header("Runtime (Private)")]
    [SerializeField, Tooltip("Debug/ince ayar için görünür bırakılabilir. İlk zıplama çıkış hızı (u/s).")]
    float _jumpV0;        // Awake’te hesaplanıyor

    [SerializeField, Tooltip("Sahnede kalan zıplama sayısı.")]
    int   _jumpsLeft;

    [SerializeField, Tooltip("Yatay hedef hız (internal).")]
    float _targetVx;

    bool  jumpHeld;
    bool  jumpCutActive;
    float jumpStartTime;

    private int  jumpCount = 0;
    private int  maxJumpCount = 2;

    public void SetBotFlag(bool v) { IsBot = v; }

    private void Awake()
    {
        PhotonNetwork.SendRate = 30;
        PhotonNetwork.SerializationRate = 30;

        float g = (2f * desiredJumpHeight) / (timeToApex * timeToApex); // u/s²
        Physics2D.gravity = new Vector2(0f, -g);

        // İlk zıplamanın çıkış hızı (hep aynı yükseklik için sabit v0)
        _jumpV0 = g * timeToApex; // ~21 u/s (4.2u / 0.40s için)

        // GravityScale’i 1’e sabitle (fast-fall/jump-cut çarpanları bundan türeyecek)
        baseGravityScale = 1f;
        gravityScale = 1f;

        playerRb = GetComponent<Rigidbody2D>();
        currentSpeed = baseMoveSpeed;
        currentJumpForce = baseJumpForce;
        gravityScale = baseGravityScale;
        externalForceAccumulator = GetComponent<ExternalForceAccumulator>();
        animationManager = GetComponent<AnimationManager>();
        sync = GetComponent<PlayerNetworkSync>();
        playerAim = GetComponent<PlayerAim>();
        _col = GetComponent<CapsuleCollider2D>();

        if (animator == null) animator = GetComponentInChildren<Animator>(true);

    }

    void Start()
    {
        if (PhotonNetwork.InRoom && photonView != null && !photonView.IsMine) 
        return;

        if (PhotonNetwork.InRoom)              // offline’da gereksiz
            PhotonNetwork.NetworkStatisticsEnabled = true;
    }

    public void InitializePlayerData(PlayerData data)
    {
        playerData = data;
        ActorNumber = data?.ActorNumber
               ?? GetComponent<EntityIdentity>()?.ActorNumber
               ?? photonView.OwnerActorNr;

        IsBot = data != null && data.IsBot;

        if (IsBot) GetComponent<PlayerAim>().overrideAim = true;

        ApplyMapBasedAttributes();
        ApplyPlayerDataEffects();
    }

    private void ApplyMapBasedAttributes()
    {
        if (GameManager.Instance == null || GameManager.Instance.selectedMap == null) return;

        foreach (var feature in GameManager.Instance.selectedMap.mapFeatures)
        {
            playerData.SetAttribute(feature.key, feature.value);
        }

        playerData.UpdateSpeedModifier("MapEffect", playerData.GetAttribute<float>("Effect_SpeedBoost", 1f));
        playerData.UpdateJumpBoost(playerData.GetAttribute<float>("Effect_JumpBoost", 1f));
        playerData.UpdateGravityMultiplier(playerData.GetAttribute<float>("Effect_GravityScale", 1f));
        playerData.SetFriction(playerData.GetAttribute<float>("Effect_Friction", 1f));
    }


    private void ApplyPlayerDataEffects()
    {
        if (playerData == null) return;

        if (playerData.GetAttribute<int>("maxJumpCount") != 0)
            maxJumpCount = playerData.GetAttribute<int>("maxJumpCount");

        currentSpeed = baseMoveSpeed * TotalSpeedMultiplier();
        currentJumpForce = baseJumpForce * playerData.JumpBoost * (gravityScale / baseGravityScale);
        gravityScale = baseGravityScale * playerData.GravityMultiplier;
    }

    void Update()
    {
        if (playerData == null) return;
        
        // REMOTE KARAKTER → HIÇBIR ŞEY YAPMA
        if (!photonView.IsMine) return;
        
        // INPUT TOPLAMA (İnsan veya Bot)
        float moveX = 0f;
        bool jump = false;
        
        if (IsBot)
        {
            var ai = GetComponent<BotAIController>();
            if (ai != null)
            {
                moveX = ai.MoveX;
                jump = ai.JumpPressed;
                
                // Bot ateş etmek istiyor mu?
                if (ai.ShouldFire)
                {
                    var wc = GetComponent<WeaponController>();
                    if (wc != null) wc.BotFire();
                }
            }
        }
        else
        {
            // İnsan input (klavye)
            moveX = Input.GetAxisRaw("Horizontal");
            jump = Input.GetKeyDown(KeyCode.W) && jumpCount < maxJumpCount;
        }
        
        // INPUT UYGULAMA
        inputVector = new Vector2(moveX, 0f);
        if (jump)
        {
            Jump();
            jumpHeld = true;
        }
        
        // YERÇEKİMİ
        if (!IsBot)
        {
            // İnsan için jump-cut
            if (Input.GetKeyUp(KeyCode.W))
            {
                jumpHeld = false;
                if (jumpCutEnabled && Time.time - jumpStartTime < jumpCutWindow && playerRb.velocity.y > minUpVelAfterCut)
                {
                    jumpCutActive = true;
                    playerRb.gravityScale = jumpCutGravityMult * gravityScale;
                }
            }
            
            // Fast-fall veya platform geçişi
            if (Input.GetKey(KeyCode.S))
            {
                if (!isGrounded)
                    playerRb.gravityScale = fastFallMultiplier * gravityScale;
                else if (currentPlatform != null)
                    DropThroughPlatform();
            }
            else
            {
                if (!jumpCutActive)
                    playerRb.gravityScale = gravityScale;
            }
        }
        else
        {
            // Bot için basit yerçekimi
            playerRb.gravityScale = gravityScale;
            
            // Bot aşağı inme
            var ai = GetComponent<BotAIController>();
            if (ai != null && ai.DropDownRequested && isGrounded && currentPlatform != null)
            {
                DropThroughPlatform();
                ai.DropDownRequested = false;
            }
        }

        // Jump-cut kapat
        if (jumpCutActive && (playerRb.velocity.y <= 0f || Time.time - jumpStartTime > jumpCutWindow))
        {
            jumpCutActive = false;
            playerRb.gravityScale = gravityScale;
        }

        // Bot yönlenme
        if (IsBot && Mathf.Abs(inputVector.x) > 0.1f)
        {
            Vector2 facingDir = new Vector2(inputVector.x, 0f).normalized;
            playerAim?.SetAim(facingDir);
        }
    }

    void FixedUpdate()
    {
        if (playerData == null) return;
        
        // --- GROUND CHECK ---
        Collider2D gcol;
        bool wasGrounded = isGrounded;
        isGrounded = GroundProbe(out gcol);
        currentPlatform = isGrounded ? gcol : null;
        
        if (isGrounded && !wasGrounded)
        {
            jumpCount = 0;
            AudioManager.Instance?.PlaySFX("player/land");
            EffectManager.Instance?.PlayVFXNetworked("FX_LandDust", transform.position, 1f);
            if (photonView.IsMine && CameraShaker.Instance != null) CameraShaker.Instance.TriggerShake(0.2f, 0.1f);
        }

        CheckFallOutOfBounds();

        if (!photonView.IsMine) return;        

        // --- 1. AYARLAR ---
        float maxSpeed = maxRunSpeed * TotalSpeedMultiplier();
        float accel = isGrounded ? groundAccel : airAccel;
        float currentVy = playerRb.velocity.y;

        // --- 2. KNOCKBACK HIZINI AL ---
        Vector2 knockbackVel = Vector2.zero;
        Vector2 windVel = Vector2.zero;

        if (externalForceAccumulator != null)
        {
            externalForceAccumulator.TickForces(Time.fixedDeltaTime);
            knockbackVel = externalForceAccumulator.CachedKnockback;
            windVel      = externalForceAccumulator.CachedContinuous;
        }

        // --- 5. DIŞ KUVVET (ÖNCE HESAPLA) ---
        Vector2 ext = knockbackVel + windVel;
        float extMul = isGrounded ? externalGroundMul : externalAirMul;
        ext *= extMul;

        // --- 3. INPUT HEDEFİ ---
        float targetRunVelocity = inputVector.x * maxSpeed;

        // --- 4. HAREKET HESABI (NO STUN) ---
        // Saf koşu hızını, GERÇEKTE eklediğin ext.x üzerinden çıkar
        float rawRunVel = playerRb.velocity.x - ext.x;

        // Knockback varken kontrol azalt (tam iptal yok)
        float kbXAbs = Mathf.Abs(knockbackVel.x);
        float kbT = Mathf.InverseLerp(kbControlStart, kbControlFull, kbXAbs);
        float controlFactor = Mathf.Lerp(1f, kbMinControlFactor, kbT);
        float effectiveAccel = accel * controlFactor;

        float currentRunVelocity = Mathf.MoveTowards(rawRunVel, targetRunVelocity, effectiveAccel * Time.fixedDeltaTime);
        currentRunVelocity = Mathf.Clamp(currentRunVelocity, -maxSpeed, maxSpeed);

        // Knockback'a karşı yürüyüşte “stun” yaratmayacak oppose limiti
        if (kbXAbs > 0.001f && Mathf.Abs(inputVector.x) > 0.01f)
        {
            bool opposesKb = Mathf.Sign(currentRunVelocity) != Mathf.Sign(knockbackVel.x);
            if (opposesKb)
            {
                // kb küçükken bile yürüyüşü kilitlemesin: limit maxSpeed tabanlı
                float maxOppose = maxSpeed * Mathf.Lerp(1f, kbMaxOpposeRatio, kbT);
                currentRunVelocity = Mathf.Clamp(currentRunVelocity, -maxOppose, maxOppose);
            }
        }

        // --- 6. SONUÇ (TOPLAMA) ---
        float finalVx = currentRunVelocity + ext.x;
        float finalVy = currentVy + ext.y;

        if (finalVy < -maxFallSpeed) finalVy = -maxFallSpeed;
        playerRb.velocity = new Vector2(finalVx, finalVy);

        HandleSurfaceEffects();
        UpdateBaseAnimation();
    }

    private void CheckFallOutOfBounds()
    {
        // Sadece Master Client bu kontrolü yapsın (DeathZone mantığıyla aynı)
        if (!PhotonNetwork.IsMasterClient) return;
        
        // Zaten ölüyse/respawn oluyorsa işlem yapma
        if (isRespawning) return;

        // Eğer Y pozisyonu -20'nin altına indiyse (Haritana göre bu sayıyı ayarla)
        // DeathZone collider'ının da altında bir değer olmalı.
        if (transform.position.y < -20f) 
        {
            // DeathZone içindeki DelayedFallEvent mantığını manuel tetikliyoruz
            StartCoroutine(TriggerFailSafeFall());
        }
    }

    private IEnumerator TriggerFailSafeFall()
    {
        // Tekrar girmesin diye kitle
        isRespawning = true; 

        // O hayattaki son vuranı bul
        int actorNumberFinal = ActorNumber;
        int viewID = photonView.ViewID;
        Vector3 fallPos = transform.position;

        int pusher = -1;
        var fallenPhotonPlayer = PhotonNetwork.CurrentRoom.GetPlayer(actorNumberFinal);
        
        // LastHitBy bilgisini çek
        if (playerData != null)
            pusher = playerData.LastHitBy;

        // Event'i gönder
        NetworkEventManager.RaiseEvent(
            EventCodes.HandleFall,
            new object[] { viewID, pusher, fallPos },
            ReceiverGroup.All
        );

        yield return null;
    }

    private void UpdateBaseAnimation()
    {
        if (animationManager == null || photonView == null) return;
        if (!photonView.IsMine) return; // Animasyonu sadece owner tarafı sürsün

        Vector2 v = playerRb.velocity;
        float speedX = Mathf.Abs(v.x);

        string baseState;

        // Havada mıyız?
        if (!isGrounded)
        {
            // Yükseliyorsa Jump, düşüyorsa Fall
            baseState = v.y > 0.1f ? "Jump" : "Fall";
        }
        else
        {
            // Yerdeyken hız düşükse Idle, değilse Run
            baseState = speedX < moveThreshold ? "Idle" : "Run";
        }

        // AnimationManager içinde zaten spam/tekrar koruması var, direkt çağırıyoruz.
        animationManager.PlayAnimation(photonView.ViewID, baseState);
    }
    
    public void ForceIdleOnRespawn()
    {
        // Fiziksel Reset
        jumpCount = 0;
        jumpCutActive = false;
        jumpHeld = false;
        _lastAnimState = "Idle";
        _speedXSmoothed = 0f;

        if (playerRb != null)
        {
            playerRb.velocity = Vector2.zero;
            playerRb.angularVelocity = 0f;
        }

        var abilityCtrl = GetComponent<AbilityController>();
        if (abilityCtrl != null) abilityCtrl.ClearAbility();

        // Network tarafında animasyon state cache'ini temizle ve herkesi Idle'a çek
        if (animationManager != null && photonView != null)
        {
            animationManager.ResetForView(photonView.ViewID);
            animationManager.PlayAnimation(photonView.ViewID, "Idle");
        }
    }

    public void ApplyBotInput(float moveX, bool jump, bool drop, bool fire)
    {
        // Sadece bot ve yerel-olmayan objede çalış
        if (!TryGetComponent<BotAIController>(out _)) return;
        if (Photon.Pun.PhotonNetwork.InRoom && photonView != null && photonView.IsMine) return;

        // Yürüyüş
        inputVector.x = Mathf.Clamp(moveX, -1f, 1f);

        // Zıplama (tek tetik)
        if (jump) Jump();

        // One-way platformdan aşağı bırak
        if (drop) DropThroughPlatform();
    }

   void Jump()
    {
        Vector2 v = playerRb.velocity;

        // 1. Durum: Yerdeyiz veya henüz hiç zıplamadık (İlk Zıplama)
        if (jumpCount == 0 || isGrounded)
        {
            // Hep aynı yükseklik → hız AYARLA
            v.y = Mathf.Max(v.y, _jumpV0);
            jumpCount = 1;

            EffectManager.Instance.PlayVFXNetworked("FX_JumpDust", transform.position, 1f);
            AudioManager.Instance?.PlaySFX("player/jump");
        }

        // 2. Durum: Havadayız ve limitimiz var (Multi Jump / Triple Jump)
        else if (jumpCount < maxJumpCount)
        {
            float g = -Physics2D.gravity.y;

            // (a) ekstra yükseklikten gereken hız
            float vyFromExtra = Mathf.Sqrt(Mathf.Max(0f, 2f * g * secondJumpExtraHeight));

            // (b) Hedef dikey hız
            float vyMatch = secondJumpVyTarget;

            // Hızı uygula
            v.y = Mathf.Max(v.y, Mathf.Max(vyFromExtra, vyMatch));
            
            // Zıplama sayacını artır
            jumpCount++;

            // Efektler (Double jump efektini 3. zıplamada da kullanıyoruz)
            EffectManager.Instance.PlayVFXNetworked("FX_DoubleJump", transform.position, 1f);
            
            // Eğer 3. zıplamaysa "TRIPLE JUMP" yazabilir, yoksa "DOUBLE JUMP"
            string text = (jumpCount == 3) ? "TRIPLE JUMP!" : "DOUBLE JUMP!";
            EffectTextManager.Instance.ShowEffectText("FX_Text_Movement", text, transform);
            
            AudioManager.Instance?.PlaySFX("player/jump");
        }
        else
        {
            // Limit doldu, zıplama yapma
            return;
        }

        // Hızı uygula
        playerRb.velocity = new Vector2(playerRb.velocity.x, v.y);

        // Jump-cut penceresini başlat
        jumpStartTime = Time.time;
        jumpCutActive = false;     
        jumpHeld = true;      
    }

    bool GroundProbe(out Collider2D hitCol)
    {
        // Ana kapsülün alt kenarına yaslanıp aşağı doğru çooook kısa bir BoxCast atıyoruz.
        Bounds b = _col.bounds;

        // Kutunun merkezi: kapsülün tabanına çok yakın (bir tık yukarı)
        Vector2 castCenter = new Vector2(b.center.x, b.min.y + 0.02f);

        // Kutunun boyutu: genişçe ve basık
        Vector2 castSize = new Vector2(groundCastWidth, groundCastHeight);

        // Aşağı çok kısa mesafe; EdgeCollider çizgisini mutlaka keser
        var hit = Physics2D.BoxCast(castCenter, castSize, 0f, Vector2.down, groundCastDistance, groundMask);

        hitCol = hit.collider;
        return hit.collider != null;
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        if (!c.gameObject.CompareTag("Platform")) return;
        currentPlatform = c.collider; // isGrounded = GroundProbe’den geliyor
    }
    void OnCollisionExit2D(Collision2D c)
    {
        if (!c.gameObject.CompareTag("Platform")) return;
        if (currentPlatform == c.collider) currentPlatform = null;
    }

    public IEnumerator ResetCollisionAfterDelay(string platformID, float delay)
    {
        yield return new WaitForSeconds(delay);
        NetworkEventManager.RaiseEvent(
            EventCodes.ResetPlatformCollision,
            platformID
        );
    }

    public void ResetPlatformCollision(string platformID)
    {
        GameObject platform = GameObject.Find(platformID);
        if (platform != null)
        {
            Collider2D platformCol = platform.GetComponent<Collider2D>();
            if (platformCol != null)
            {
                Physics2D.IgnoreCollision(GetComponent<Collider2D>(), platformCol, false);
            }
        }
    }

    void DropThroughPlatform()
    {
        Collider2D myCollider = GetComponent<Collider2D>();
        Physics2D.IgnoreCollision(myCollider, currentPlatform, true);

        string platformID = currentPlatform.gameObject.name;
        StartCoroutine(ResetCollisionAfterDelay(platformID, 0.5f));
    }

    private float TotalSpeedMultiplier()
    {
        if (playerData == null) return 1f;

        float totalMultiplier = 1.0f;
        totalMultiplier *= 1.0f / playerData.WeightMultiplier;
        foreach (var modifier in playerData.SpeedModifiers.Values)
        {
            totalMultiplier *= modifier;
        }
        totalMultiplier *= playerData.SpeedBoost;
        return totalMultiplier;
    }

    // PlayerController.cs'nin en altına ekle:

    private void HandleSurfaceEffects()
    {
        if (playerData == null) return;

        // Varsayılan hız çarpanı (Normal zemin = 1.0)
        float targetModifier = 1.0f; 

        // Yerdeysek ve bastığımız bir platform varsa kontrol et
        if (isGrounded && currentPlatform != null)
        {
            // PlatformProperties'i bul (Kendi üzerinde veya parent'ında olabilir)
            var props = currentPlatform.GetComponent<PlatformProperties>() 
                    ?? currentPlatform.GetComponentInParent<PlatformProperties>();
            
            if (props != null)
            {
                if (props.hasSlowEffect)
                {
                    // Yavaşlatma oranı: 0.8f (%20 yavaşlar). 
                    // Eskiden 0.5f'ti, çok fazlaydı. Bunu buradan ayarlayabilirsin.
                    targetModifier = 0.5f; 
                }
                else if (props.hasSpeedBoost)
                {
                    // Hızlandırma oranı
                    targetModifier = 1.1f; 
                }
            }
        }

        // "TemporaryBuff" anahtarını her karede güncelliyoruz.
        // Böylece normal zemine bastığın AN hızın 1.0f'e geri döner.
        playerData.UpdateSpeedModifier("TemporaryBuff", targetModifier);
    }
}

