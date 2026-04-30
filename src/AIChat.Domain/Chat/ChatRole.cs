namespace AIChat.Domain.Chat;

// Common chat roles. Keeping this enum small makes provider mapping explicit.
public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}
