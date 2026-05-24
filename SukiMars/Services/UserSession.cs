namespace SukiMars.Services;

public sealed class UserSession
{
    public int UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}