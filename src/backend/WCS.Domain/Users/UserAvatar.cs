namespace WCS.Domain.Users;

/// <summary>
/// A user's avatar image, kept in the database (no file path exists, so nothing outside the table can be reached through it) in its own table so that
/// loading an account never loads image bytes. At most one per account; removed together with the account. The bytes were validated by the application
/// (type sniffed from the content, size and dimension limits) before they got here.
/// </summary>
public class UserAvatar
{
    private UserAvatar() { }

    public static UserAvatar Create(Guid userId, string contentType, byte[] data, DateTimeOffset now) =>
        new() { UserId = userId, ContentType = contentType, Data = data, UpdatedAt = now };

    public Guid UserId { get; private set; }
    public string ContentType { get; private set; } = default!;
    public byte[] Data { get; private set; } = default!;
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Replace(string contentType, byte[] data, DateTimeOffset now) { ContentType = contentType; Data = data; UpdatedAt = now; }
}
