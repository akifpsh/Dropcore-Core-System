using Photon.Pun;
using Photon.Realtime;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using DG.Tweening;

/// <summary>
/// Core game logic controller: handles game flow, scene transitions, and settings application.
/// </summary>
public class GameManager : MonoBehaviourPun
{
    public static GameManager Instance;

    public ModeData activeModeData;
    public MapData selectedMap;
    public bool isGameOver = false; 
    private int playersFinishedLoading = 0;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            PhotonNetwork.AutomaticallySyncScene = true;
        }
        else  Destroy(gameObject);
    }

    public static void SetInstance(GameManager instance)
    {
        if (Instance == null)
        {
            Instance = instance;
            DontDestroyOnLoad(instance.gameObject);
        }
    }
    private void Start()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SteamAchievementHelper.Init();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// Resets runtime game data for mode/map.
    /// </summary>
    public void ResetGameManagerState()
    {
        isGameOver = false;
        activeModeData = null;
        selectedMap = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu")
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Destroy(gameObject);
            return;
        }
        // LOBBY - Sadece Master Client işlemleri
        if (scene.name == "Lobby")
        {
            if (SceneTransitionManager.Instance != null)
            {
                SceneTransitionManager.Instance.RevealScene();
            }
            
            if (PhotonNetwork.IsMasterClient)
            {
                PhotonNetwork.CurrentRoom.IsOpen = true;
                PhotonNetwork.CurrentRoom.IsVisible = true;
                StartCoroutine(WaitForLobbyPanelManager());
            }
        }
        // GAMEPLAY - HEM Master HEM Clientlar için çalışır
        else if (scene.name == "Gameplay")
        {
            ClearAllBufferedRpcs();
            
            // Oda ayarlarını sadece Master yapar
            if (PhotonNetwork.IsMasterClient)
            {
                PhotonNetwork.CurrentRoom.IsOpen = false;
                PhotonNetwork.CurrentRoom.IsVisible = false;

                // Haritayı Master yükler (PhotonInstantiate ile diğerlerine gider)
                if (MapManager.Instance != null && selectedMap != null)
                {
                    string mapName = selectedMap.mapPrefab.name;
                    MapManager.Instance.LoadMapMaster(mapName);
                }
            }

            // DİKKAT: Artık oyunu hemen başlatmıyoruz (StartPreparation SİLİNDİ).
            // Onun yerine "Ben yüklendim, hazırım" sinyali göndereceğiz.
            StartCoroutine(SendReadySignalWhenMapIsReady());
        }
    }

    // Sahne yüklendikten sonra biraz bekleyip Master'a "Ben Hazırım" der.
    private IEnumerator SendReadySignalWhenMapIsReady()
    {
        // MapManager ve diğer scriptlerin Awake/Start yapabilmesi için kısa bir bekleme
        yield return new WaitForSeconds(0.5f); 
        
        // Master Client'a "Benim sahnem yüklendi" bilgisini yolla
        photonView.RPC("RPC_ClientIsReady", RpcTarget.MasterClient);
    }

    [PunRPC]
    public void RPC_ClientIsReady()
    {
        // Sadece Master Client bu sayıyı takip eder
        playersFinishedLoading++;

        int playersInRoom = PhotonNetwork.CurrentRoom.PlayerCount;

        // Eğer ODAKİ HERKES (veya daha fazlası) hazırsa
        if (playersFinishedLoading >= playersInRoom)
        {
            // Sayaçları sıfırla
            playersFinishedLoading = 0;
            // Herkese "Siyah ekranı kaldır ve oyuna başla" emri ver
            photonView.RPC("RPC_StartGameplaySync", RpcTarget.All);
        }
    }

    [PunRPC]
    public void RPC_StartGameplaySync()
    {
        // 1. SceneTransitionManager'daki siyah ekranı kaldır (Manuel açılış)
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.RevealScene(); // Bunu SceneTransitionManager'a eklemiştin
        }

        // 2. Eğer Master ise Timer'ı başlat ve oyunu akışa sok
        if (PhotonNetwork.IsMasterClient)
        {
            TimerManager.Instance.StartPreparation();
        }
    }

    private void ClearAllBufferedRpcs()
    {
        var allViews = FindObjectsOfType<PhotonView>();
        int count = 0;
        foreach (var pv in allViews)
        {
            // Sadece kendimize ait olan (IsMine) objelerin veya Sahne objelerinin RPC'lerini silebiliriz.
            // Başkasının objesine dokunmaya çalışırsan o kırmızı hataları alırsın.
            if (pv.ViewID > 0 && (pv.IsMine || PhotonNetwork.IsMasterClient)) 
            {
                // Sahiplik kontrolü yaparak silmeyi dene
                try 
                {
                    PhotonNetwork.RemoveRPCs(pv);
                    count++;
                }
                catch
                {
                    // Hata olursa oyunu durdurmasın, devam etsin
                }
            }
        }
        // Debug.Log($"[GameManager] Cleared RPCs from {count} views.");
    }

    private IEnumerator WaitForLobbyPanelManager()
    {
        LobbyPanelManager lpm = null;
        yield return new WaitUntil(() => (lpm = FindObjectOfType<LobbyPanelManager>()) != null);
    }

    public float GetMapValue(string key, float defaultValue = 0f)
    {
        if (selectedMap == null) return defaultValue;
        var feature = selectedMap.mapFeatures.FirstOrDefault(f => f.key == key);
        return feature != null ? feature.value : defaultValue;
    }

    public float GetStaticValue(string key, float defaultValue = 0f)
    {
        if (activeModeData?.staticSettings == null) return defaultValue;
        var setting = activeModeData.staticSettings.Find(s => s.key == key);
        return setting != null ? setting.value : defaultValue;
    }

    public T GetDynamicValue<T>(string settingName, T defaultValue = default)
    {
        if (activeModeData == null || activeModeData.dynamicSettings == null) return defaultValue;
       
        var dynamicSetting = activeModeData.dynamicSettings.FirstOrDefault(s => s.settingName == settingName);
        if (dynamicSetting != null)
        {
            try
            {
                if (typeof(T) == typeof(bool)) return (T)(object)dynamicSetting.isOn;
                if (typeof(T) == typeof(int)) return (T)(object)dynamicSetting.currentValue;
                if (typeof(T) == typeof(float)) return (T)(object)(float)dynamicSetting.currentValue;
            }
            catch
            {
                DebugManager.Warning($"[GameManager] Failed to convert dynamic setting: {settingName}");
            }
        }
        return defaultValue;
    }

    [PunRPC]
    public void RPC_UpdateAllSettings(string[] keys, int[] values, bool[] toggles)
    {
        if (activeModeData == null || activeModeData.dynamicSettings == null) return;

        foreach (var setting in activeModeData.dynamicSettings)
        {
            int index = Array.IndexOf(keys, setting.settingName);
            if (index >= 0)
            {
                setting.currentValue = values[index];
                setting.isOn = toggles[index];
            }
        }
    }

    public void StartGame()
    {
        if (activeModeData == null || SurpriseBoxManager.Instance == null || AbilityBoxManager.Instance == null) return;

        ApplyMapEffectsToAllPlayers();
        ModeManager.Instance.InitializeMode(activeModeData.modeName);
        PlayerManager.Instance.ResetAllReadyStatus();

        int gameDuration = Instance.GetDynamicValue<int>("GameDuration", 300);
        TimerManager.Instance.StartTimer(gameDuration);

        SurpriseBoxManager.Instance.InitializeDropSettings();
        AbilityBoxManager.Instance.InitializeAbilitySettings();

        /*if (activeModeData.dynamicSettings != null)
        {
            foreach (var dynamicSetting in activeModeData.dynamicSettings)
            {
                Debug.Log($"[GameManager] Dynamic Setting Loaded: {dynamicSetting.settingName} = {dynamicSetting.currentValue} (Default: {dynamicSetting.defaultValue})");
            }
        }

        // Statik ayarlar� logla (varsa)
        if (activeModeData.staticSettings != null)
        {
            foreach (var staticSetting in activeModeData.staticSettings)
            {
                Debug.Log($"[GameManager] Static Setting Loaded: Key = {staticSetting.key}, Value = {staticSetting.value}");
            }
        }*/
    }

    public void ApplyMapEffectsToAllPlayers()
    {
        if (selectedMap == null) return;

        foreach (var playerData in PlayerManager.Instance.GetAllPlayers())
        {
            foreach (var feature in selectedMap.mapFeatures)
            {
                playerData.SetAttribute(feature.key, feature.value);
            }
        }
    }

    public IGameMode GetCurrentGameMode()
    {
        return GameModeFactory.GetGameMode(activeModeData.modeName);
    }

    public void EndGame(PlayerData winner)
    {
        if (isGameOver) return;
        isGameOver = true;
        
        PhotonView.Get(Instance).RPC("RPC_StopGame", RpcTarget.All);

        if (winner != null && GameOverPanelManager.Instance != null)
        {
            PhotonView.Get(GameOverPanelManager.Instance).RPC(
                "RPC_ShowGameOverPanel",
                RpcTarget.All,
                winner.ActorNumber,                        // 1) actorNumber
                winner.NickName ?? string.Empty            // 2) isim (boş da olabilir)
            );
        }

        if (winner != null)
        {
            NetworkEventManager.RaiseEvent(
                EventCodes.PlayEndGameSound,
                new object[] { winner.ActorNumber },
                ReceiverGroup.All
            );
        }
    }

    [PunRPC]
    public void RPC_StopGame()
    {
        AbilityBoxManager.Instance?.StopAbilityRoutine();
        SurpriseBoxManager.Instance?.StopDropRoutine();
        isGameOver = true;
    }

    public void HandleRetryTimeout()
    {
        if (PlayerManager.Instance.AreAllPlayersReady())
        {
            StartCoroutine(RetryGameRoutine());
        }
        else
        {
            PhotonView.Get(this).RPC("RPC_ReturnToLobby", RpcTarget.All);
        }
        BotRegistry.Instance?.ClearAll();
    }

    private IEnumerator RetryGameRoutine()
    {
        yield return new WaitForSeconds(2f);
        PhotonView.Get(this).RPC("RPC_CleanupScene", RpcTarget.All);
        yield return new WaitForSeconds(1f);
        PhotonView.Get(this).RPC("RPC_RetryGame", RpcTarget.MasterClient);
    }

    [PunRPC]
    public void RPC_RetryGame()
    {
        isGameOver = false;
        PlayerManager.Instance.ResetPlayersForRetry();
        PhotonView.Get(GameOverPanelManager.Instance).RPC("RPC_HideGameOverPanel", RpcTarget.All);
        StartGame();
    }

    [PunRPC]
    public void RPC_CleanupScene()
    {
        // 1. UI Temizliği (Herkes kendi ekranında yapar)
        if (PlayerInfoPanelManager.Instance != null)
        {
            PlayerInfoPanelManager.Instance.ResetAllCardsVisuals();
        }
        
        // Yetenek ikonlarını temizle
        foreach (var ac in FindObjectsOfType<AbilityController>())
        {
            if (ac.GetComponent<PhotonView>().IsMine)
            {
                ac.ClearAbility();
            }
        }

        // 2. Master Client Nesne Temizliği
        if (!PhotonNetwork.IsMasterClient) return;

        string[] netTags = { "DropBox", "Weapon", "Duck" }; // Silinecek etiketler
        
        foreach (var tag in netTags)
        {
            // Koleksiyonu kopyalayarak (ToArray) döngü sırasında liste değişimi hatasını önle
            var objects = GameObject.FindGameObjectsWithTag(tag);
            
            foreach (var obj in objects)
            {
                if (obj == null) continue;

                PhotonView pv = obj.GetComponent<PhotonView>();
                // PhotonView yoksa düz Destroy, varsa PhotonNetwork.Destroy
                if (pv == null)
                {
                    Destroy(obj);
                }
                else
                {
                    // Nesne geçerli bir ViewID'ye sahipse yok et
                    if (pv.ViewID > 0 && !pv.IsRoomView) // RoomView (Scene Object) değilse
                    {
                        PhotonNetwork.Destroy(obj);
                    }
                }
            }
        }

        // Managerleri temizle
        BulletPool.Instance?.ClearAllBullets();
        AbilityBoxManager.Instance?.ClearAllAbilityBoxes();
        SurpriseBoxManager.Instance?.ClearAllBoxes();
    }

    [PunRPC]
    public void RPC_ReturnToLobby()
    {
        ResetGameManagerState();
        
        // PlayerManager temizliği
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.RemoveAllBots();
            PlayerManager.Instance.ClearPrefabs();
            PlayerManager.Instance.ResetAllReadyStatus();
        }

        // DOTween temizliği
        DG.Tweening.DOTween.KillAll(true);
        DG.Tweening.DOTween.Clear(true);
        Resources.UnloadUnusedAssets();

        GameObject dotweenGO = GameObject.Find("[DOTween]");
        if (dotweenGO != null) Destroy(dotweenGO);

        // Sahne Yükleme Çağrısı
        SceneTransitionManager.Instance.LoadSceneNetworked("Lobby");
    }

}
