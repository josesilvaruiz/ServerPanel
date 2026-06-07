namespace ServerPanel.Models;

public class SavedCmdGroup
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Owner { get; set; } = "";   // email del usuario o "all" para compartido
    public int SortOrder { get; set; }
    public List<SavedCmd> Commands { get; set; } = new();
}

public class SavedCmd
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    public SavedCmdGroup Group { get; set; } = default!;
    public string Text { get; set; } = "";
    public int SortOrder { get; set; }
}
