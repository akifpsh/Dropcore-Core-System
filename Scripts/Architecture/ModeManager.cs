using Photon.Pun;
using UnityEngine;

/// <summary>
/// Coordinates game mode activation, effect application, and winner determination.
/// </summary>
public class ModeManager : MonoBehaviourPun
{
    public static ModeManager Instance { get; private set; }
    private IGameMode currentMode;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void InitializeMode(string modeName)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        photonView.RPC("RPC_StartMode", RpcTarget.All, modeName);
    }

    [PunRPC]
    private void RPC_StartMode(string modeName)
    {
        currentMode = GameModeFactory.GetGameMode(modeName);

        if (currentMode != null)
            currentMode.StartMode();
    }

    public void ApplyModeEffects(PlayerData fallenPlayer, PlayerData opponentPlayer)
    {
        if (currentMode != null)
            currentMode.ApplyModeEffects(fallenPlayer, opponentPlayer);
    }

    public void DetermineWinnerAndEndGame()
    {
        if (currentMode != null)
        {
            var winner = currentMode.DetermineWinner();
            if (winner != null)
                GameManager.Instance.EndGame(winner);
        }
    }
}