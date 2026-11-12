/// <summary>
/// Shared contract for objects the player can interact with.
/// Visual prompts and highlighting can be added later without changing each object's gameplay logic.
/// </summary>
public interface IPlayerInteractable
{
    bool CanInteract(PlayerCarrySystem player);
    string GetInteractionPrompt(PlayerCarrySystem player);
    void Interact(PlayerCarrySystem player);
}
