namespace ServerPanel.Models;

public enum BoardTaskState
{
    ToDo = 0,
    Doing = 1,
    Done = 2,
}

/// <summary>A card of the task board (/tareas): a sprint board with three columns.</summary>
public class BoardTask
{
    public long Id { get; set; }

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public BoardTaskState State { get; set; }

    public string? AssignedTo { get; set; }

    /// <summary>Position inside its column (lower first).</summary>
    public int Order { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>A person of the task board team: tasks are assigned to one of them by name.</summary>
public class BoardMember
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; }
}
