using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary >
/// Manages player data, synchronization, and core gameplay attributes across all clients.
/// </summary>
public class PlayerManager : MonoBehaviourPunCallbacks
{
    public static PlayerManager Instance { get; private set; }

    private List<PlayerData> players = new List<PlayerData>(); // Oyuncu listesi
    private Dictionary<int, GameObject> playerPrefabs = new Dictionary<int, GameObject>();
    public int AlivePlayersCount => players.Count(p => p.Health > 0);
    public event System.Action OnPlayerListChanged;

    // Initial attribute sync sadece 1 kez çalışsın diye
    private readonly HashSet<string> _initialSyncSent = new();
    private readonly HashSet<string> _initialSyncApplied = new();


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        players.Clear(); 
        playerPrefabs.Clear();
        
        PhotonNetwork.AddCallbackTarget(this);
        if (PhotonNetwork.InRoom)
            SyncExistingPlayers();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        PhotonNetwork.RemoveCallbackTarget(this);
    }

    public override void OnJoinedRoom()
    {
        SyncExistingPlayers();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        AddPlayer(new PlayerData(newPlayer.ActorNumber, newPlayer.NickName), newPlayer);
    }

    private void SyncExistingPlayers()
    {
        _initialSyncSent.Clear();
        _initialSyncApplied.Clear();
        players.Clear();
        foreach (var p in PhotonNetwork.PlayerList)
            AddPlayer(new PlayerData(p.ActorNumber, p.NickName));
        OnPlayerListChanged?.Invoke();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu")
        {
            // Olay dinlemeyi bırak
            SceneManager.sceneLoaded -= OnSceneLoaded;

            // Listeleri temizle
            players.Clear();
            playerPrefabs.Clear();

            // Kendini yok et
            if (this.gameObject != null)
                Destroy(this.gameObject);

            // Statik referansı temizle
            Instance = null;
        }
    }

    public static void SetInstance(PlayerManager instance)
    {
        Instance = instance;
        if (instance != null)
        {
            DontDestroyOnLoad(instance.gameObject);
        }
    }

    public void AddPlayer(PlayerData playerData, Player photonPlayer)
    {
        if (!players.Any(p => p.ActorNumber == playerData.ActorNumber))
        {
            if (photonPlayer.CustomProperties.TryGetValue("ReadyStatus", out object ready))
                playerData.ReadyStatus = (bool)ready;

            players.Add(playerData);
            OnPlayerListChanged?.Invoke();
        }
    }

    public void AddPlayer(PlayerData playerData)
    {
        var photonPlayer = PhotonNetwork.CurrentRoom.GetPlayer(playerData.ActorNumber);
        if (photonPlayer == null)
            return;

        AddPlayer(playerData, photonPlayer);
    }

    public void RemovePlayer(int actorNumber)
    {
        var player = players.FirstOrDefault(p => p.ActorNumber == actorNumber);
        if (player != null)
        {
            players.Remove(player);
            PlayerInfoPanelManager.Instance?.RemoveCard(actorNumber);
            OnPlayerListChanged?.Invoke();
        }
    }

    [PunRPC]
    public void RemovePlayerRPC(int actorNumber)
    {
        RemovePlayer(actorNumber); 
    }

    public PlayerData GetPlayer(int actorNumber) => players.FirstOrDefault(p => p.ActorNumber == actorNumber);
    public List<PlayerData> GetAllPlayers() => players;

    public void AddPlayerPrefab(int actorNumber, GameObject prefab)
    {
        if (!playerPrefabs.ContainsKey(actorNumber))
            playerPrefabs[actorNumber] = prefab;
    }

    public GameObject GetPlayerPrefab(int actorNumber)
    {
        if (playerPrefabs.TryGetValue(actorNumber, out var prefab)) return prefab;
        return null;
    }

    public void DestroyPlayerPrefab(int actorNumber)
    {
        if (playerPrefabs.TryGetValue(actorNumber, out GameObject prefab))
        {
            if (PhotonNetwork.IsMasterClient)
                PhotonNetwork.Destroy(prefab); 

            playerPrefabs.Remove(actorNumber); 
        }
    }

    public void ClearPrefabs()
    {
        playerPrefabs.Clear();
    }

    public void SetReadyStatus(bool isReady)
    {
        var props = new Hashtable { { "ReadyStatus", isReady } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        var pd = players.FirstOrDefault(p => p.ActorNumber == targetPlayer.ActorNumber);
        if (pd == null) return;

        bool dirty = false;

        if (changedProps.ContainsKey("ReadyStatus"))
        {
            pd.ReadyStatus = (bool)changedProps["ReadyStatus"];
            dirty = true;
        }

        if (changedProps.ContainsKey("LastHitBy"))
        {
            pd.LastHitBy = (int)changedProps["LastHitBy"];
        }

        if (changedProps.ContainsKey("LastHitTime"))
        {
            pd.LastHitTime = (float)changedProps["LastHitTime"];
            dirty = true;
        }

        if (changedProps.ContainsKey("steamID") && pd != null)
        {
            pd.SteamID = (string)changedProps["steamID"];
            OnPlayerListChanged?.Invoke();
        }

        if (dirty)
            OnPlayerListChanged?.Invoke();
    }

    public bool AreAllPlayersReady()
    {
        // Sadece gerçek oyunculara bak
        var humans = players.Where(p => !p.IsBot).ToList();

        // Oda tamamen botsa (teorik), burayı istersen false yaparsın.
        if (humans.Count == 0)
            return true;

        return humans.All(p => p.ReadyStatus);
    }

    public void ResetAllReadyStatus()
    {
        foreach (var pd in players)
            pd.ReadyStatus = false;

        var props = new Hashtable { { "ReadyStatus", false } };
        foreach (var pl in PhotonNetwork.PlayerList)
            pl.SetCustomProperties(props);

        OnPlayerListChanged?.Invoke();  
    }

    public void HandleResetCustomAndReady()
    {
        foreach (var pd in players)
        {
            pd.CurrentPistol = "Maverick";
            pd.CurrentSkin = "Shaya";
            pd.CurrentSkill = "NoWeight";
            pd.ReadyStatus = false;
        }
        CustomizationPanelManager.Instance?.RefreshAllZones();
    }

    public IEnumerable<PlayerData> GetAlivePlayers() => players.Where(p => p.Health > 0);
    public PlayerData GetPlayerWithHighestHealth() => players.Where(p => p.Health > 0).OrderByDescending(p => p.Health).FirstOrDefault();

   public void UpdateLastHitBy(int targetActorNumber, int attackerActorNumber)
    {
        // RPC YERİNE EVENT ATIYORUZ
        NetworkEventManager.RaiseEvent(
            EventCodes.UpdateLastHitBy,
            new object[] { targetActorNumber, attackerActorNumber },
            ReceiverGroup.All
        );
    }

    public void UpdateLastHitByLocally(int targetActorNumber, int attackerActorNumber)
    {
        var player = GetPlayer(targetActorNumber);
        if (player != null)
        {
            player.LastHitBy = attackerActorNumber;
            // Eğer LastHitBy değişince tetiklenmesi gereken başka bir logic varsa buraya ekleyebilirsin.
        }
    }

    public void UpdatePlayerAttribute(int actorNumber, string attributeKey, object newValue)
    {
        var player = GetPlayer(actorNumber);
        if (player == null) return;

        player.SetAttribute(attributeKey, newValue);

        PhotonView playerManagerPhotonView = GetComponent<PhotonView>();
        if (playerManagerPhotonView == null) return;

        photonView.RPC("UpdatePlayerAttributes", RpcTarget.All, actorNumber, attributeKey, newValue);
   }

    [PunRPC]
    public void UpdatePlayerAttributes(int actorNumber, string attributeKey, object newValue)
    {
        var player = GetPlayer(actorNumber);
        if (player == null) return;
        
        // SADECE VERİYİ GÜNCELLE, BAŞKA RPC ATMA
        player.SetAttribute(attributeKey, newValue);
    }

    [PunRPC]
    public void SyncPlayerInitialAttribute(int actorNumber, string attributeKey, int value)
    {
        string fixedKey = attributeKey.ToLower();
        string k = $"{actorNumber}:{fixedKey}";

        if (_initialSyncApplied.Contains(k)) return;
        _initialSyncApplied.Add(k);

        var player = GetPlayer(actorNumber);
        if (player != null)
        {
            player.SetAttribute(fixedKey, value);
            PlayerInfoPanelManager.Instance.UpdateCardValue(actorNumber, fixedKey, value);
        }
    }

    public void SyncPlayerInitialAttributeOnce(int actorNumber, string attributeKey, int value)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        string fixedKey = attributeKey.ToLower();
        string k = $"{actorNumber}:{fixedKey}";

        if (_initialSyncSent.Contains(k)) return;
        _initialSyncSent.Add(k);

        photonView.RPC(nameof(SyncPlayerInitialAttribute), RpcTarget.All, actorNumber, fixedKey, value);
    }

    public PlayerData CompareAccuracy(List<PlayerData> list)
    {
        if (list == null || list.Count == 0) return null;
        float max = list.Max(p => p.GetAccuracy());
        var top = list.Where(p => Mathf.Approximately(p.GetAccuracy(), max)).ToList();
        return top.Count == 1 ? top.First() : top.OrderBy(p => Random.value).FirstOrDefault();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        int actorNumber = otherPlayer.ActorNumber;

        DestroyPlayerPrefab(actorNumber); 
        RemovePlayer(actorNumber);

        if (SceneManager.GetActiveScene().name == "Lobby" && CustomizationPanelManager.Instance != null)
        {
            CustomizationPanelManager.Instance.RemovePlayerZone(actorNumber);
        }
    }

    [PunRPC]
    public void ResetPlayersForRetry()
    {
        foreach (var player in players)
        {
            int startHealth = GameManager.Instance.GetDynamicValue<int>("StartHealth", 10);
            player.Health = startHealth;
            player.Level = 0;
            player.Point = 0;
            player.ReadyStatus = false;

            GameObject playerPrefab = GetPlayerPrefab(player.ActorNumber);
            if (playerPrefab != null)
            {
                playerPrefab.transform.position = PlayerSpawner.Instance.GetSpawnPosition(player.ActorNumber);
                playerPrefab.SetActive(true);

                WeaponController weaponController = playerPrefab.GetComponent<WeaponController>();
                if (weaponController != null)
                {
                    weaponController.ClearSecondarySlot();
                    PhotonView view = weaponController.GetComponent<PhotonView>();
                    if (view != null)
                    {
                        NetworkEventManager.RaiseEvent(
                            EventCodes.ClearSecondarySlot,
                            new object[] { view.ViewID },
                            ReceiverGroup.Others
                        );
                    }
                }
            }
        }
        OnPlayerListChanged?.Invoke();
    }

    public void IncreaseHealth(int actorNumber, int amount) => ModifyHealth(actorNumber, amount);
    public void DecreaseHealth(int actorNumber, int amount) => ModifyHealth(actorNumber, -amount);

   public void ModifyHealth(int actorNumber, int amount)
    {
        var player = GetPlayer(actorNumber);
        if (player == null) return;

        int newHealth = Mathf.Max(player.Health + amount, 0);

        if (PhotonNetwork.IsMasterClient)
        {
            player.Health = newHealth;
        }

        NetworkEventManager.RaiseEvent(
            EventCodes.UpdateHealth,
            new object[] { actorNumber, newHealth },
            ReceiverGroup.All
        );
    }

    public void UpdateHealthLocally(int actorNumber, int newHealth)
    {
        var player = GetPlayer(actorNumber);
        if (player == null) return;

        // 1. Veriyi güncelle (SetAttribute RPC atmaz, sadece veriyi yazar)
        player.Health = newHealth;
        player.SetAttribute("health", newHealth);

        // 2. UI Güncelle
        if (PlayerInfoPanelManager.Instance != null)
        {
            PlayerInfoPanelManager.Instance.UpdateCardValue(actorNumber, "health", newHealth);
            PlayerInfoPanelManager.Instance.MarkEliminated(actorNumber, newHealth <= 0);
        }

        // 3. Efektler (Sadece Kendim İçin)
        if (PhotonNetwork.LocalPlayer.ActorNumber == actorNumber && newHealth < player.Health) // Hasar aldıysa
        {
            // Varsa hasar alma sesi/efekti buraya
        }

        // 4. Ölüm ve Oyun Bitiş Kontrolü (SADECE MASTER YAPAR)
        if (newHealth <= 0)
        {
            var prefab = GetPlayerPrefab(actorNumber);
            if (prefab != null) prefab.SetActive(false);

            if (PhotonNetwork.IsMasterClient)
            {
                string mode = GameManager.Instance?.activeModeData?.modeName.ToUpper();
                // Survivor, Escape veya OSOK modları için tek kişi kalınca bitir
                if (mode == "SURVIVOR" || mode == "ESCAPE" || mode == "OSOK")
                {
                    if (AlivePlayersCount <= 1) ModeManager.Instance.DetermineWinnerAndEndGame();
                }
                else // Diğer modlar (Deathmatch vb.) puan/süre ile biter, ama herkes ölürse bitebilir
                {
                    ModeManager.Instance.DetermineWinnerAndEndGame();
                }
            }
        }
    }

    [PunRPC]
    public void EliminatePlayerRPC(int actorNumber)
    {
        var prefab = GetPlayerPrefab(actorNumber);
        if (prefab != null)
        {
            prefab.SetActive(false); 
        }
    }

    [PunRPC]
    public void RPC_UpdateHealthUI(int actorNumber, int newHealth)
    {
        PlayerInfoPanelManager.Instance.UpdateCardValue(actorNumber, "health", newHealth);
    }

    public void IncreaseLevel(int actorNumber, int amount) => ModifyLevel(actorNumber, amount);
    public void DecreaseLevel(int actorNumber, int amount) => ModifyLevel(actorNumber, -amount);

    public void ModifyLevel(int actorNumber, int amount)
    {
        var player = GetPlayer(actorNumber);
        if (player == null) return;

        // Maksimum seviye kontrolü
        int maxLevel = 100; // Varsayılan, aşağıda WeaponManager'dan da alabilirsin
        if (WeaponManager.Instance != null) 
            maxLevel = WeaponManager.Instance.GetShuffledWeaponListCount() - 1;

        int newLevel = Mathf.Clamp(player.Level + amount, 0, maxLevel);

        NetworkEventManager.RaiseEvent(
            EventCodes.UpdateLevel,
            new object[] { actorNumber, newLevel },
            ReceiverGroup.All
        );
    }

    public void UpdateLevelLocally(int actorNumber, int newLevel)
    {
        var player = GetPlayer(actorNumber);
        if (player == null) return;

        player.Level = newLevel;
        player.SetAttribute("level", newLevel);

        if (PlayerInfoPanelManager.Instance != null)
            PlayerInfoPanelManager.Instance.UpdateCardValue(actorNumber, "level", newLevel);

        // Efektler
        if (PhotonNetwork.LocalPlayer.ActorNumber == actorNumber)
        {
            var prefab = GetPlayerPrefab(actorNumber);
            if (prefab != null)
            {
                AudioManager.Instance.PlaySFX("player/point_up");
                EffectTextManager.Instance.ShowEffectText("FX_Text_Point", "LEVEL UP!", prefab.transform);
                EffectManager.Instance.PlayVFX("FX_PointUp", prefab.transform.position, 1f);
            }
        }

        // Silah Değişimi (Master Yönetir)
        if (PhotonNetwork.IsMasterClient)
        {
            var prefab = GetPlayerPrefab(actorNumber);
            if (prefab != null)
            {
                var wc = prefab.GetComponent<WeaponController>();
                var data = WeaponManager.GetWeaponByLevel(newLevel) ?? WeaponManager.Instance.GetWeapon("Maverick");
                if (wc != null) wc.RequestAssignWeapon(data.weaponName, WeaponSlot.Secondary);
            }
            
            // Max level kontrolü
            int maxLevel = WeaponManager.Instance.GetShuffledWeaponListCount() - 1;
            if (newLevel >= maxLevel) ModeManager.Instance.DetermineWinnerAndEndGame();
        }
    }

    [PunRPC]
    public void RPC_UpdateLevelUI(int actorNumber, int newLevel)
    {
        PlayerInfoPanelManager.Instance.UpdateCardValue(actorNumber, "level", newLevel);
    }

    public void IncreasePoint(int actorNumber, int amount) => ModifyPoint(actorNumber, amount);
    public void DecreasePoint(int actorNumber, int amount) => ModifyPoint(actorNumber, -amount);

    public void ModifyPoint(int actorNumber, int amount)
    {
        var player = GetPlayer(actorNumber);
        if (player == null) return;

        int newPoint = Mathf.Max(player.Point + amount, 0);

        NetworkEventManager.RaiseEvent(
            EventCodes.UpdatePoint,
            new object[] { actorNumber, newPoint },
            ReceiverGroup.All
        );
    }

    public void UpdatePointLocally(int actorNumber, int newPoint)
    {
        var player = GetPlayer(actorNumber);
        if (player == null) return;

        player.Point = newPoint;
        player.SetAttribute("point", newPoint);

        if (PlayerInfoPanelManager.Instance != null)
            PlayerInfoPanelManager.Instance.UpdateCardValue(actorNumber, "point", newPoint);

        // Efektler
        if (PhotonNetwork.LocalPlayer.ActorNumber == actorNumber)
        {
            var prefab = GetPlayerPrefab(actorNumber);
            if (prefab != null)
            {
                EffectTextManager.Instance.ShowEffectText("FX_Text_Point", "+1 POINT", prefab.transform);
            }
        }
        
        // Puanla bitiş kontrolü (Master)
        if (PhotonNetwork.IsMasterClient)
        {
            int maxPoint = GameManager.Instance.GetDynamicValue("MaxPoint", 20);
            if (newPoint >= maxPoint) ModeManager.Instance.DetermineWinnerAndEndGame();
        }
    }

    [PunRPC]
    private void RPC_DetermineWinner(int winnerActorNumber)
    {
        PlayerData winner = PlayerManager.Instance.GetPlayer(winnerActorNumber);
        if (winner == null) return;
        ModeManager.Instance.DetermineWinnerAndEndGame();
    }

    [PunRPC]
    public void RPC_UpdatePointUI(int actorNumber, int newPoint)
    {
        PlayerInfoPanelManager.Instance.UpdateCardValue(actorNumber, "point", newPoint);
    }

    public void AddBotPlayer(int actorNumber, string nickName)
    {
        if (players.Any(p => p.ActorNumber == actorNumber)) return;

        var bot = new PlayerData(actorNumber, nickName, readyStatus: true);
        // Botların başlangıç değerlerini istersen burada özelleştirebilirsin
        bot.IsBot = true;  
        players.Add(bot);
        OnPlayerListChanged?.Invoke();
    }

    public void RemoveAllBots()
    {
        // Sadece bot olan PlayerData’ları listeden at
        players.RemoveAll(p => p.IsBot);
        OnPlayerListChanged?.Invoke();
    }
}
