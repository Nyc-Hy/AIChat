namespace AIChat.Application.Projects;

public sealed record ProjectLoadSnapshot(
    string HealthText,
    string ProfileText,
    string ActivityText,
    string RecommendationText);
