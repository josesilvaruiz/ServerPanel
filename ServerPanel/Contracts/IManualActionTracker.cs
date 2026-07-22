namespace ServerPanel.Contracts;

// Registra cuándo se disparó un stop/restart manual desde el panel, por servidor,
// para que las alertas de caída puedan distinguir "lo apagué yo" de una caída real.
public interface IManualActionTracker
{
    void MarkAction(string serverName);

    bool WasRecentlyActedOn(string serverName, TimeSpan window);
}
