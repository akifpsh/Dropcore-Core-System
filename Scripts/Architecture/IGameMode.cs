public interface IGameMode
{
    void StartMode();
    void ApplyModeEffects(PlayerData fallenPlayer, PlayerData opponentPlayer);
    PlayerData DetermineWinner();
}
