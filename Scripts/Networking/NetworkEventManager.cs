using UnityEngine;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections.Generic;

public class NetworkEventManager : MonoBehaviourPunCallbacks
{
    public static NetworkEventManager Instance { get; private set; }

    // EventCode → çoklu callback’e izin veren delegate
    private readonly Dictionary<byte, Action<object>> handlers = new Dictionary<byte, Action<object>>();

    private const byte ChannelCount = 4;

    private static readonly Dictionary<byte, (bool reliable, byte channel)> eventMap = new()
    {
        // ──────────────── Kanal 0: Kritik Oyun Mantığı (reliable) ────────────────
        { EventCodes.PlayerHit,                (true,  0) },
        { EventCodes.RequestAssignWeapon,      (true,  0) },
        { EventCodes.SyncAssignWeapon,         (true,  0) },
        { EventCodes.RequestPickupWeapon,      (true,  0) },
        { EventCodes.SyncPickedUpWeapon,       (true,  0) },
        { EventCodes.RequestDropWeapon,        (true,  0) },
        { EventCodes.SyncDroppedWeapon,        (true,  0) },
        { EventCodes.RequestDestroyBox,        (true,  0) },
        { EventCodes.ClearSecondarySlot,       (true,  0) },
        { EventCodes.RequestDestroyAbilityBox, (true,  0) },
        { EventCodes.SyncAbilityBox,           (true,  0) },
        { EventCodes.HandleFall,               (true,  0) },
        { EventCodes.HandleRespawn,            (true,  0) },
        { EventCodes.ResetCustomAndReady,      (true,  0) },
        { EventCodes.UpdateHealth,             (true,  0) }, 
        { EventCodes.UpdateLevel,              (true,  0) },
        { EventCodes.UpdatePoint,              (true,  0) },
        { EventCodes.UpdateLastHitBy,          (true,  0) },

        // ──────────────── Kanal 1: Spawn & Setup (reliable) ────────────────
        { EventCodes.CreateCard,               (true,  1) },
        { EventCodes.ApplySkin,                (true,  1) },
        { EventCodes.ApplySkill,               (true,  1) },
        { EventCodes.SyncOwnership,            (true,  1) },
        { EventCodes.Lobby_AddBotSlot,         (true,  1) },
        { EventCodes.Lobby_RemoveBotSlot,      (true,  1) },

        // ──────────────── Kanal 2: Durum & Pozisyon (unreliable) ────────────────
        { EventCodes.UpdateGroundState,        (false, 2) },
        { EventCodes.DropThrough,              (false, 2) },
        { EventCodes.ResetPlatformCollision,   (false, 2) },
        { EventCodes.ExplosionKnockback,       (true,  0) },
        { EventCodes.DuckKnockback,            (false, 2) },
        { EventCodes.Rewind_Sync,              (false, 2) },
        { EventCodes.Bullet_Fire,              (false, 2) },
        { EventCodes.Bullet_Activate,          (false, 2) },
        { EventCodes.Bullet_Deactivate,        (false, 2) },
        { EventCodes.UpdateAim,                (false, 2) },

        // ──────────────── Kanal 3: Görsel & Efekt (unreliable) ────────────────
        { EventCodes.PlayVFX,                  (false, 3) },
        { EventCodes.PlayVFXAttached,          (false, 3) },
        { EventCodes.ShowEffectText,           (false, 3) },
        { EventCodes.ShowStaticEffectText,     (false, 3) },
        { EventCodes.PlayAnimation,            (false, 3) },
        { EventCodes.PlaySFX,                  (false, 3) },
        { EventCodes.PlayEndGameSound,         (false, 3) },
        { EventCodes.CameraShake,              (false, 3) },
        { EventCodes.ApplyWindForce,           (false, 3) },
        { EventCodes.ApplyWindEvent,           (false, 3) },
        { EventCodes.StopWindEvent,            (false, 3) },
        { EventCodes.ActivateHyperDash,        (false, 3) },
        { EventCodes.DeactivateHyperDash,      (false, 3) },
        { EventCodes.PlaySabotageEffect,       (false, 3) },
        { EventCodes.Sabotage_PlayEffect,      (false, 3) },
        { EventCodes.OSOK_SetVisibility,       (false, 3) },
    };

    private static readonly Dictionary<byte, float> throttleIntervals = new()
    {
        // Görsel & Efekt
        { EventCodes.PlayVFX,               200f },
        { EventCodes.PlayVFXAttached,       100f },
        { EventCodes.ShowEffectText,        200f },
        { EventCodes.ShowStaticEffectText,  200f },
        { EventCodes.PlaySFX,               200f },
        { EventCodes.PlayEndGameSound,      500f },
        { EventCodes.CameraShake,           200f },
        { EventCodes.PlayAnimation,         100f },

        // Durum & Pozisyon
        { EventCodes.UpdateGroundState,     100f },
        { EventCodes.DropThrough,           100f },
        { EventCodes.ResetPlatformCollision,100f },
        { EventCodes.ExplosionKnockback,    100f },
        { EventCodes.DuckKnockback,         100f },
        { EventCodes.Rewind_Sync,           50f  },
        { EventCodes.Bullet_Fire,           50f  },
        { EventCodes.Bullet_Activate,       200f },
        { EventCodes.Bullet_Deactivate,     200f },
        { EventCodes.OSOK_SetVisibility,    100f },
        { EventCodes.UpdateHealth,          50f},
        { EventCodes.UpdateAim,             50f },

        // Rüzgâr & Diğer Efektler
        { EventCodes.ApplyWindEvent,        200f },
        { EventCodes.StopWindEvent,         200f },
    };

    private static readonly Dictionary<byte, float> lastSentTime = new Dictionary<byte, float>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            PhotonNetwork.NetworkingClient.LoadBalancingPeer.ChannelCount = 4;
            PhotonNetwork.NetworkingClient.EventReceived += OnEventReceived;
        }
        else Destroy(gameObject);
    }

    void Start()
    {
        RegisterHandler(EventCodes.Bullet_Fire, OnBulletFireEvent);
        RegisterHandler(EventCodes.RequestExplosion, OnRequestExplosionEvent);
        RegisterHandler(EventCodes.ExplosionKnockback, OnExplosionKnockbackEvent);
        RegisterHandler(EventCodes.DuckKnockback, OnDuckKnockbackEvent);
        RegisterHandler(EventCodes.RequestAssignWeapon, OnRequestAssignWeapon);
        RegisterHandler(EventCodes.SyncAssignWeapon, OnSyncAssignWeapon);
        RegisterHandler(EventCodes.RequestPickupWeapon, OnRequestPickupWeapon);
        RegisterHandler(EventCodes.SyncPickedUpWeapon, OnSyncPickedUpWeapon);
        RegisterHandler(EventCodes.RequestDropWeapon, OnRequestDropWeapon);
        RegisterHandler(EventCodes.SyncDroppedWeapon, OnSyncDroppedWeapon);
        RegisterHandler(EventCodes.RequestDestroyBox, OnRequestDestroyBox);
        RegisterHandler(EventCodes.ClearSecondarySlot, OnClearSecondarySlot);
        RegisterHandler(EventCodes.RequestDestroyAbilityBox, OnRequestDestroyAbilityBox);
        RegisterHandler(EventCodes.HandleRespawn, OnHandleDeathAndRespawn);
        RegisterHandler(EventCodes.HandleFall, OnHandleFall);
        RegisterHandler(EventCodes.SyncAbilityBox, OnSyncAbilityBox);
        RegisterHandler(EventCodes.PlaySFX, OnPlaySFX);
        RegisterHandler(EventCodes.PlayEndGameSound, OnPlayEndGameSound);
        RegisterHandler(EventCodes.PlayVFX, OnPlayVFX);
        RegisterHandler(EventCodes.PlayVFXAttached, OnPlayVFXAttached);
        RegisterHandler(EventCodes.ShowEffectText, OnShowEffectText);
        RegisterHandler(EventCodes.ShowStaticEffectText, OnShowStaticText);
        RegisterHandler(EventCodes.CameraShake, OnCameraShake);
        RegisterHandler(EventCodes.PlayAnimation, OnPlayAnimation);
        RegisterHandler(EventCodes.PlayerHit, OnPlayerHitEvent);
        RegisterHandler(EventCodes.ApplyWindEvent, OnApplyWindEvent);
        RegisterHandler(EventCodes.ResetCustomAndReady, OnResetCustomAndReadyEvent);
        //RegisterHandler(EventCodes.UpdateGroundState, OnUpdateGroundStateEvent);
        RegisterHandler(EventCodes.ResetPlatformCollision, OnResetPlatformCollisionEvent);
        RegisterHandler(EventCodes.DropThrough, OnDropThroughEvent);
        RegisterHandler(EventCodes.OSOK_SetVisibility, OnOSOKSetVisibilityEvent);
        RegisterHandler(EventCodes.Lobby_AddBotSlot, OnLobbyAddBotSlot);
        RegisterHandler(EventCodes.Lobby_RemoveBotSlot, OnLobbyRemoveBotSlot);
        RegisterHandler(EventCodes.UpdateHealth, OnUpdateHealth);
        RegisterHandler(EventCodes.UpdateLevel,  OnUpdateLevel);
        RegisterHandler(EventCodes.UpdatePoint,  OnUpdatePoint);
        RegisterHandler(EventCodes.UpdateLastHitBy, OnUpdateLastHitBy);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            PhotonNetwork.NetworkingClient.EventReceived -= OnEventReceived;
            Instance = null;
        }
    }

    private void OnEventReceived(EventData ev)
    {
        if (handlers.TryGetValue(ev.Code, out var callback))
        {
            object realData = ev.CustomData;

            if (ev.CustomData is object[] arr
                && arr.Length >= 2
                && arr[arr.Length - 1] is double sentTime)
            {
                realData = arr[0];
                double latency = PhotonNetwork.Time - sentTime;
            }

            callback.Invoke(realData);
        }
    }


    /// <summary>
    /// Belirli bir event kodu için callback ekler
    /// </summary>
    public void RegisterHandler(byte eventCode, Action<object> callback)
    {
        if (handlers.ContainsKey(eventCode))
            handlers[eventCode] += callback;
        else
            handlers[eventCode] = callback;
    }

    /// <summary>
    /// Daha önce eklenmiş callback’i kaldırır
    /// </summary>
    public void UnregisterHandler(byte eventCode, Action<object> callback)
    {
        if (!handlers.ContainsKey(eventCode)) return;
        handlers[eventCode] -= callback;
        if (handlers[eventCode] == null)
            handlers.Remove(eventCode);
    }

    // —— THROTTLED RAISEEVENT —— //

    /// <summary>
    /// Tüm event’lerinizi bu metotla çağırın. Reliability ve channel eventMap’ten geliyor,
    /// ayrıca yukarıda tanımlı interval’lere göre throttling uygulanıyor.
    /// </summary>
    public static void RaiseEvent(byte eventCode, object customData, ReceiverGroup receivers = ReceiverGroup.All)
    {
        bool throttled = false;
        if (throttleIntervals.TryGetValue(eventCode, out float minMs))
        {
            float nowMs = Time.time * 1000f;
            if (lastSentTime.TryGetValue(eventCode, out float last) && nowMs - last < minMs)
            {
                throttled = true;
            }
            else lastSentTime[eventCode] = nowMs;
        }
        if (throttled) return;

        NetworkProfiler.Instance?.TrackEvent(eventCode.ToString());

        double sentTime = PhotonNetwork.Time;
        object[] payloadWithTs = new object[] { customData, sentTime };

        var (isReliable, channelId) = eventMap.TryGetValue(eventCode, out var cfg)
            ? cfg
            : (true, (byte)0);

        int maxChannels = PhotonNetwork.NetworkingClient.LoadBalancingPeer.ChannelCount;
        if (channelId >= maxChannels) channelId = 0;

        var options = new RaiseEventOptions { Receivers = receivers };
        var sendOpts = new SendOptions { Reliability = isReliable, Channel = channelId };
        PhotonNetwork.RaiseEvent(eventCode, payloadWithTs, options, sendOpts);
    }

    private void OnBulletFireEvent(object rawData)
    {
        var d = (object[])rawData;
        Vector3 pos = (Vector3)d[0];
        Vector3 dir3 = (Vector3)d[1];
        float speed = (float)d[2];
        float life = (float)d[3];
        string name = (string)d[4];
        int shooter = (int)d[5];

        var bullet = BulletPool.Instance.GetBullet(pos, Quaternion.identity);
        var bComp = bullet.GetComponent<Bullet>();
        Vector2 dir = new Vector2(dir3.x, dir3.y).normalized;
        bComp.Initialize(shooter, dir, speed, life, name);

        var shooterObj = PlayerManager.Instance.GetPlayerPrefab(shooter);
        if (shooterObj != null)
        {
            Collider2D shooterCol = shooterObj.GetComponent<Collider2D>();
            Collider2D bulletCol = bullet.GetComponent<Collider2D>();

            // Eğer referanslar sağlamsa, çarpışmayı yoksay
            if (shooterCol != null && bulletCol != null)
            {
                Physics2D.IgnoreCollision(shooterCol, bulletCol, true);
            }
        }
    }

    private void OnRequestExplosionEvent(object rawData)
    {
        var d = (object[])rawData;

        Vector3 pos = (Vector3)d[0];
        float radius = (float)d[1];
        float maxKb = (float)d[2];
        float minKb = (float)d[3];
        int excludeId = (d.Length >= 5) ? (int)d[4] : -1;

        if (!PhotonNetwork.IsMasterClient) return;

        ExplosionManager.Instance.TriggerExplosion((Vector2)pos, radius, maxKb, minKb, 0.4f, excludeId);
    }

    void OnExplosionKnockbackEvent(object rawData)
    {
        var d = (object[])rawData;
        int viewID = (int)d[0];
        Vector2 direction = (Vector2)d[1];
        float deltaV = (float)d[2];
        float duration = (float)d[3];

        // 5. eleman varsa bullet olarak kabul edeceğiz
        bool isBullet = false;
        if (d.Length >= 5)
        {
            // int/bool/byte gelebilir, güvenli parse
            try
            {
                if (d[4] is bool b) isBullet = b;
                else isBullet = System.Convert.ToInt32(d[4]) == 1;
            }
            catch { isBullet = false; }
        }

        var targetView = PhotonView.Find(viewID);
        if (targetView == null) return;
        if (!targetView.IsMine) return;

        Vector2 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.zero;

        var forceAcc = targetView.GetComponent<ExternalForceAccumulator>();
        if (forceAcc == null) return;

        if (isBullet)
        {
            // Bullet: duration yok, anlık kick + damping
            forceAcc.AddBulletKickDeltaV(dir * deltaV);
        }
        else
        {
            // Explosion (ve diğerleri): mevcut davranış aynen
            forceAcc.AddImpulseDeltaV(dir * deltaV, duration);
        }
    }

    private void OnDuckKnockbackEvent(object rawData)
    {
        var d = (object[])rawData;
        int viewID = (int)d[0];
        Vector2 direction = (Vector2)d[1];
        float deltaV = (float)d[2];
        float stunDuration = (float)d[3];

        var targetView = PhotonView.Find(viewID);
        if (targetView == null || !targetView.IsMine) return;

        Vector2 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.zero;
        
        var forceAcc = targetView.GetComponent<ExternalForceAccumulator>();
        if (forceAcc != null)
        {
            forceAcc.AddImpulseDeltaV(dir * deltaV, stunDuration);
        }
    }

    private void OnRequestAssignWeapon(object rawData)
    {
        var d = (object[])rawData;
        int viewID = (int)d[0];
        string name = (string)d[1];
        int slotIndex = (int)d[2];
        WeaponSlot slot = (WeaponSlot)slotIndex;

        var ctrl = PhotonView.Find(viewID).GetComponent<WeaponController>();
        ctrl.ApplySyncedWeapon(name, slot);

        RaiseEvent(
            EventCodes.SyncAssignWeapon,
            new object[] { viewID, name, slotIndex },
            ReceiverGroup.Others
        );
    }

    private void OnSyncAssignWeapon(object rawData)
    {
        var d = (object[])rawData;
        int viewID = (int)d[0];
        string name = (string)d[1];
        int slotIndex = (int)d[2];
        WeaponSlot slot = (WeaponSlot)slotIndex;

        PhotonView.Find(viewID).GetComponent<WeaponController>().ApplySyncedWeapon(name, slot);
    }

    private void OnRequestPickupWeapon(object rawData)
    {
        var d = (object[])rawData;
        int viewID = (int)d[0];
        string name = (string)d[1];
        int ammo = (int)d[2];
        int dropViewID = (int)d[3];

        var ctrl = PhotonView.Find(viewID).GetComponent<WeaponController>();
        if (ctrl == null) return;

        ctrl.PickupWeapon(WeaponManager.Instance.GetWeapon(name), ammo, dropViewID);
    }

    private void OnSyncPickedUpWeapon(object rawData)
    {
        var d = (object[])rawData;
        int viewID = (int)d[0];
        string name = (string)d[1];
        int ammo = (int)d[2];

        var ctrl = PhotonView.Find(viewID).GetComponent<WeaponController>();
        if (ctrl == null) return;

        ctrl.ApplySyncedWeapon(name, WeaponSlot.Secondary, ammo);
    }

    private void OnRequestDropWeapon(object rawData)
    {
        var d = (object[])rawData;
        int dropperID = (int)d[0];
        Vector3 pos = (Vector3)d[1];
        WeaponType type = (WeaponType)(byte)d[2];
        string name = (string)d[3];
        int ammo = (int)d[4];

        string path = $"Prefabs/WeaponPrefabs/{type}";
        GameObject dropObj = PhotonNetwork.Instantiate(path, pos, Quaternion.identity);
        dropObj.tag = "Weapon";

        var wp = dropObj.GetComponent<WeaponPickup>();
        wp.SetDroppedData(name, ammo);

        int pickupViewID = dropObj.GetComponent<PhotonView>().ViewID;
        RaiseEvent(
          EventCodes.SyncDroppedWeapon,
          new object[] { pickupViewID, name, ammo },
          ReceiverGroup.Others
        );
    }

    private void OnSyncDroppedWeapon(object rawData)
    {
        var d = (object[])rawData;
        int pickupID = (int)d[0];
        string name = (string)d[1];
        int ammo = (int)d[2];

        var wp = PhotonView.Find(pickupID)?.GetComponent<WeaponPickup>();
        if (wp != null) wp.SetDroppedData(name, ammo);
    }

    private void OnRequestDestroyBox(object rawData)
    {
        var d = (object[])rawData;
        int viewID = (int)d[0];
        var boxPV = PhotonView.Find(viewID);
        if (boxPV != null)
            SurpriseBoxManager.Instance.DestroyBox(boxPV.gameObject);
    }

    private void OnClearSecondarySlot(object rawData)
    {
        var d = (object[])rawData;
        int viewID = (int)d[0];
        var ctrl = PhotonView.Find(viewID)?.GetComponent<WeaponController>();
        if (ctrl != null)
            ctrl.ClearSecondarySlot();
    }

    private void OnSyncAbilityBox(object rawData)
    {
        var d = (object[])rawData;
        int viewID = (int)d[0];
        string ability = (string)d[1];

        var pv = PhotonView.Find(viewID);
        if (pv == null) return;

        var box = pv.GetComponent<AbilityBox>();
        var data = AbilityBoxManager.Instance.abilityList
                        .Find(a => a.abilityName == ability);
        if (box != null && data != null)
            box.SetAbility(data);
    }

    private void OnRequestDestroyAbilityBox(object raw)
    {
        var data = (object[])raw;
        int viewID = (int)data[0];

        var pv = PhotonView.Find(viewID);
        if (pv == null) return;

        GameObject box = pv.gameObject;
        AbilityBoxManager.Instance.DestroyAbilityBox(box);
    }

    private void OnHandleDeathAndRespawn(object raw)
    {
        var data = (object[])raw;
        int viewID = (int)data[0];
        Vector3 pos = (Vector3)data[1];

        var pv = PhotonView.Find(viewID);
        if (pv == null) return;

        // ✅ EĞER BU BİZİM KARAKTERİMİZSE, coroutine'i çalıştırma
        // Çünkü lokal client zaten TriggerRespawn ile halletti
        if (pv.IsMine)
            return;

        int actorNumber = -1;
        var pc = pv.GetComponent<PlayerController>();
        if (pc != null)
            actorNumber = pc.ActorNumber;
        else if (pv.Owner != null)
            actorNumber = pv.Owner.ActorNumber;

        if (actorNumber != -1)
        {
            string modeName = GameManager.Instance.activeModeData.modeName.ToUpper();

            // Survivor / Escape / OSOK: canı 0 olan oyuncu asla respawn olmasın
            if (modeName == "SURVIVOR" || modeName == "ESCAPE" || modeName == "OSOK")
            {
                var pdata = PlayerManager.Instance.GetPlayer(actorNumber);
                if (pdata == null || pdata.Health <= 0)
                {
                    return;
                }
            }
        }

        GameObject go = pv.gameObject;

        // ✅ Remote client için respawn coroutine
        RespawnManager.Instance.StartCoroutine(
            RespawnManager.Instance.RespawnCoroutine(go, pos, null)
        );
    }

    private void OnHandleFall(object raw)
    {
        var data = (object[])raw;
        int viewID = (int)data[0];
        int pusher = (int)data[1];
        Vector3 pos = (Vector3)data[2];
        var pv = PhotonView.Find(viewID);
        if (pv == null) return;
        FallManager.Instance.HandleFallEvent(viewID, pusher, pos);
    }

    private void OnPlaySFX(object raw)
    {
        var data = (object[])raw;
        string key = (string)data[0];
        Vector3 pos = (Vector3)data[1];
        float vol = (float)data[2];
        AudioManager.Instance.PlayClipAtPosition(key, pos, vol);
    }

    private void OnPlayEndGameSound(object raw)
    {
        var data = (object[])raw;
        int winnerActor = (int)data[0];
        int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
        string clip = (localActor == winnerActor)
            ? "general/victory"
            : "general/defeat";
        AudioManager.Instance.PlaySFX(clip);
    }

    private void OnPlayVFX(object raw)
    {
        var data = (object[])raw;
        string name = (string)data[0];
        Vector3 pos = (Vector3)data[1];
        float dur = (float)data[2];
        EffectManager.Instance.PlayVFX(name, pos, dur);
    }

    private void OnPlayVFXAttached(object raw)
    {
        var data = (object[])raw;
        string name = (string)data[0];
        int viewID = (int)data[1];
        float dur = (float)data[2];

        PhotonView pv = PhotonView.Find(viewID);
        if (pv != null)
            EffectManager.Instance.PlayVFXAttached(name, pv.transform, dur);
    }

    private void OnShowEffectText(object raw)
    {
        var d = (object[])raw;
        string prefabName = (string)d[0];
        string text = (string)d[1];
        Vector3 startPos = (Vector3)d[2];
        Vector3 offset = (Vector3)d[3];
        int vid = (int)d[4];
        float duration = (float)d[5];
        
        EffectTextManager.Instance.HandleShowEffectText(
            prefabName, text, startPos, offset, vid, duration);
    }

    private void OnShowStaticText(object raw)
    {
        var d = (object[])raw;
        string prefabName = (string)d[0];
        string text = (string)d[1];
        Vector3 worldPos = (Vector3)d[2];
        float duration = (float)d[3];

        EffectTextManager.Instance.HandleShowStaticEffectText(
            prefabName, text, worldPos, duration);
    }

    private void OnCameraShake(object raw)
    {
        var data = (object[])raw;
        float magnitude = (float)data[0];
        float duration = (float)data[1];
        CameraShaker.Instance.TriggerShake(magnitude, duration);
    }

    private void OnPlayAnimation(object raw)
    {
        var data = (object[])raw;
        int viewID = (int)data[0];
        string anim = (string)data[1];

        var view = PhotonView.Find(viewID);
        if (view == null) return;

        var animComp = view.GetComponentInChildren<Animator>(true);
        if (animComp == null) return;

        // Animator inactive ise Unity hata basıyor; aktif olana kadar görseli atlamak daha güvenli.
        if (!animComp.isActiveAndEnabled || !animComp.gameObject.activeInHierarchy) return;

        animComp.Play(anim, 0, 0f);
    }

    private void OnPlayerHitEvent(object raw)
    {
        var d = (object[])raw;
        int fallenActor = (int)d[0];    // Vurulan kişi
        int shooterActor = (int)d[1];   // Vuran kişi
        float hitTime = (float)d[2];
        float force = (float)d[3];
        Vector2 knockDir = new Vector2((float)d[4], (float)d[5]);
    
        // Veriyi güncelle (Herkes için)
        var pd = PlayerManager.Instance.GetPlayer(fallenActor);
        if (pd != null)
        {
            pd.LastHitBy = shooterActor;
            pd.LastHitTime = hitTime;
        }

        bool applyPhysics = false;

        // A) Vurulan benim karakterim
        if (PhotonNetwork.LocalPlayer.ActorNumber == fallenActor) 
        {
            applyPhysics = true;
        }
        // B) Ben Master'ım ve vurulan bir BOT (Botlar Master'ın sorumluluğundadır)
        else if (PhotonNetwork.IsMasterClient)
        {
            // PlayerManager'dan bu actor'ün bot olup olmadığını kontrol et
            var victimData = PlayerManager.Instance.GetPlayer(fallenActor);
            if (victimData != null && victimData.IsBot)
            {
                applyPhysics = true;
            }
        }

        if (applyPhysics)
        {
            var go = PlayerManager.Instance.GetPlayerPrefab(fallenActor);
            if (go != null)
            {
                var pc = go.GetComponent<PlayerController>();
                var rb = go.GetComponent<Rigidbody2D>();
                var wc = go.GetComponent<WeaponController>();
                
                if (pc != null && rb != null && wc != null)
                {
                    // Knockback uygula
                    wc.ApplyKnockback(pc, rb, force, knockDir);
                }
            }
        }

        if (PhotonNetwork.LocalPlayer.ActorNumber == shooterActor)
        {
            var attackerObj = PlayerManager.Instance.GetPlayerPrefab(shooterActor);
            var attackerData = PlayerManager.Instance.GetPlayer(shooterActor);
            attackerData.RegisterHit();
            if (CameraShaker.Instance != null)
            {
                // knockDir: vurulanın itildiği yön. Shooter’da his için tersine kick
                Vector2 dir = -knockDir;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
                dir.Normalize();

                // force: event’ten geliyor. Küçük bir scale yeterli
                float mag = Mathf.Clamp(force * 0.006f, 0.03f, 0.10f);
                CameraShaker.Instance.TriggerPunch(dir, mag, 0.08f);
            }
            if (attackerData.TotalHits % 7 == 0)
                EffectTextManager.Instance.ShowEffectText(
                    "FX_Text_Kill", "HIT!", attackerObj.transform
                );
        }
    }

    private void OnApplyWindEvent(object raw)
    {
        WindController.Instance?.SendMessage("OnApplyWindEvent", raw);
    }

    private void OnStopWindEvent(object raw)
    {
        if (raw == null)
            WindController.Instance.SendMessage("OnStopWindEvent");
        else
            WindController.Instance.SendMessage("OnStopWindEvent", raw);
    }

    private void OnResetCustomAndReadyEvent(object rawData)
    {
        PhotonNetwork.LocalPlayer.SetCustomProperties(
            new Hashtable {
                { "skin",  "Shaya"    },    // default skin
                { "skill", "NoWeight" },    // default skill
                { "pistol","Maverick"},    // default pistol
                { "ReadyStatus", false }    // reset ready
            }
        );
        
        PlayerManager.Instance.HandleResetCustomAndReady();
    }

    /*
    private void OnUpdateGroundStateEvent(object rawData)
    {
        object[] arr = (object[])rawData;
        bool grounded = (bool)arr[0];
        int viewID = (int)arr[1];

        var pv = PhotonView.Find(viewID);
        if (pv == null) return;

        var pc = pv.GetComponent<PlayerController>();
        if (pc != null)
            pc.isGrounded = grounded;
    }
    */

    private void OnResetPlatformCollisionEvent(object rawData)
    {
        string platformID = (string)rawData;

        foreach (var pc in FindObjectsOfType<PlayerController>())
        {
            pc.ResetPlatformCollision(platformID);
        }
    }

    private void OnDropThroughEvent(object rawData)
    {
        object[] data = (object[])rawData;
        string platformID = (string)data[0];
        float duration = (float)data[1];

        foreach (var pc in FindObjectsOfType<PlayerController>())
        {
            var platform = GameObject.Find(platformID);
            if (platform != null)
            {
                var col = platform.GetComponent<Collider2D>();
                if (col != null)
                    Physics2D.IgnoreCollision(pc.GetComponent<Collider2D>(), col, true);
                pc.StartCoroutine(pc.ResetCollisionAfterDelay(platformID, duration));
            }
        }
    }

    private void OnOSOKSetVisibilityEvent(object data)
    {
        var arr = (object[])data;
        int id = (int)arr[0];
        int stateIdx = (int)arr[1];
        var pvTarget = PhotonView.Find(id);
        if (pvTarget == null) return;
        var visCtrl = pvTarget.GetComponent<OSOKVisibilityController>();
        if (visCtrl == null) return;
        visCtrl.SetVisibility((OSOKVisibilityController.VisibilityState)stateIdx, false);
    }

    private void OnLobbyAddBotSlot(object rawData)
    {
        if (CustomizationPanelManager.Instance != null)
            CustomizationPanelManager.Instance.ApplyAddBotSlotFromNetwork(rawData);
    }

    private void OnLobbyRemoveBotSlot(object rawData)
    {
        if (CustomizationPanelManager.Instance != null)
            CustomizationPanelManager.Instance.ApplyRemoveBotSlotFromNetwork(rawData);
    }

    private void OnUpdateHealth(object raw)
    {
        object[] data = (object[])raw;
        int actorNumber = (int)data[0];
        int newHealth = (int)data[1];

        // PlayerManager'a "Bu işlemi yerel olarak yap" diyoruz
        if (PlayerManager.Instance != null)
            PlayerManager.Instance.UpdateHealthLocally(actorNumber, newHealth);
    }

    private void OnUpdateLevel(object raw)
    {
        object[] data = (object[])raw;
        int actorNumber = (int)data[0];
        int newLevel = (int)data[1];

        if (PlayerManager.Instance != null)
            PlayerManager.Instance.UpdateLevelLocally(actorNumber, newLevel);
    }

    private void OnUpdatePoint(object raw)
    {
        object[] data = (object[])raw;
        int actorNumber = (int)data[0];
        int newPoint = (int)data[1];

        if (PlayerManager.Instance != null)
            PlayerManager.Instance.UpdatePointLocally(actorNumber, newPoint);
    }

    private void OnUpdateLastHitBy(object raw)
    {
        var data = (object[])raw;
        int targetActor = (int)data[0];
        int attackerActor = (int)data[1];

        if (PlayerManager.Instance != null)
            PlayerManager.Instance.UpdateLastHitByLocally(targetActor, attackerActor);
    }

}
