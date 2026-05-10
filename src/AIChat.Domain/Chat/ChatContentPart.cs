namespace AIChat.Domain.Chat;

public sealed class ChatContentPart
{
    public string Type { get; set; } = "text";
    public string Text { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string DataBase64 { get; set; } = "";
    public string SourcePath { get; set; } = "";

    public static ChatContentPart TextPart(string text) => new()
    {
        Type = "text",
        Text = text
    };

    public static ChatContentPart ImagePart(string mediaType, string dataBase64, string sourcePath = "") => new()
    {
        Type = "image",
        MediaType = mediaType,
        DataBase64 = dataBase64,
        SourcePath = sourcePath
    };
}
