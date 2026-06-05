namespace ServerPanel.Services;

public class ThemeState
{
    private string _theme = "default";

    public string Theme => _theme;

    public event Action? OnChange;

    public void Set(string theme)
    {
        _theme = theme;
        OnChange?.Invoke();
    }
}
