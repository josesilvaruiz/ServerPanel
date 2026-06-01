namespace ServerPanel.Components.Pages.Players.Modals;

public enum ModalAction
{
    Kick,
    Mute,
    Gag,
    Ban,
    Unban
}

public record ModalResult(string Reason, int Duration, ModalAction Action);
