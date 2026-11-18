using WCS.Application.Auth;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Users;

namespace WCS.Tests.Auth;

/// <summary>Pure rules of the user profile: avatar content validation and e-mail / organization normalization (no database).</summary>
public class AvatarAndProfileRulesTests
{
    // ---- fixtures: structurally complete PNG / JPEG (the validator checks structure and dimensions, not pixel data) ----
    public static byte[] Png(int w, int h, bool withIend = true)
    {
        var b = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        foreach (var v in new[] { w, h }) b.AddRange([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);
        b.AddRange([8, 2, 0, 0, 0, 0xAA, 0xBB, 0xCC, 0xDD]);
        b.AddRange([0, 0, 0, 4, (byte)'I', (byte)'D', (byte)'A', (byte)'T', 1, 2, 3, 4, 0, 0, 0, 0]);
        if (withIend) b.AddRange([0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82]);
        return [.. b];
    }

    public static byte[] Jpeg(int w, int h, bool withEoi = true)
    {
        var b = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0 };
        b.AddRange([0xFF, 0xC0, 0x00, 0x11, 8, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1]);
        b.AddRange([0xFF, 0xDA, 0x00, 0x0C, 3, 1, 0, 2, 0x11, 3, 0x11, 0, 0x3F, 0, 1, 2, 3]);
        if (withEoi) b.AddRange([0xFF, 0xD9]);
        return [.. b];
    }

    private static string Code(byte[] data) => Assert.Throws<AppException>(() => AvatarImage.Validate(data)).Code;

    [Fact]
    public void A_complete_png_and_a_complete_jpeg_are_accepted_with_the_type_taken_from_the_content()
    {
        var p = AvatarImage.Validate(Png(64, 48)); Assert.Equal(("image/png", 64, 48), (p.ContentType, p.Width, p.Height));
        var j = AvatarImage.Validate(Jpeg(200, 100)); Assert.Equal(("image/jpeg", 200, 100), (j.ContentType, j.Width, j.Height));
    }

    [Fact] public void An_empty_upload_is_refused() => Assert.Equal("AVATAR_EMPTY", Code([]));

    [Fact]
    public void An_upload_at_the_limit_passes_and_one_byte_over_is_refused()
    {
        var png = Png(64, 64); var head = png[..^12]; var tail = png[^12..];
        byte[] Sized(int total) => [.. head, .. new byte[total - head.Length - tail.Length], .. tail];
        Assert.Equal(64, AvatarImage.Validate(Sized(AvatarImage.MaxBytes)).Width);
        Assert.Equal("AVATAR_TOO_LARGE", Code(Sized(AvatarImage.MaxBytes + 1)));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' onload='alert(1)'></svg>")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("GIF89a......")]
    [InlineData("RIFF....WEBPVP8 ")]
    [InlineData("just some text pretending to be a picture.png")]
    public void Scriptable_or_unsupported_formats_are_refused_whatever_the_name_says(string content) =>
        Assert.Equal("AVATAR_UNSUPPORTED_TYPE", Code(System.Text.Encoding.ASCII.GetBytes(content)));

    [Fact] public void A_png_without_its_end_marker_is_not_a_valid_image() => Assert.Equal("AVATAR_INVALID_IMAGE", Code(Png(64, 64, withIend: false)));
    [Fact] public void A_jpeg_without_its_end_marker_is_not_a_valid_image() => Assert.Equal("AVATAR_INVALID_IMAGE", Code(Jpeg(64, 64, withEoi: false)));
    [Fact] public void A_png_signature_followed_by_garbage_is_not_a_valid_image() => Assert.Equal("AVATAR_INVALID_IMAGE", Code([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));
    [Fact] public void A_jpeg_without_a_frame_header_is_not_a_valid_image() => Assert.Equal("AVATAR_INVALID_IMAGE", Code([0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x04, 0, 0, 0xFF, 0xD9]));

    [Theory]
    [InlineData(16, 64)] [InlineData(64, 16)] [InlineData(4000, 64)] [InlineData(64, 5000)]
    public void Pixel_dimensions_outside_32_to_2048_are_refused(int w, int h)
    {
        Assert.Equal("AVATAR_DIMENSIONS", Code(Png(w, h)));
        Assert.Equal("AVATAR_DIMENSIONS", Code(Jpeg(w, h)));
    }

    [Theory]
    [InlineData("op@example.com", true)] [InlineData("first.last@sub.example.co", true)] [InlineData("a+tag@ex-ample.org", true)]
    [InlineData("", false)] [InlineData("no-at.example.com", false)] [InlineData("two@@example.com", false)] [InlineData("a@b", false)]
    [InlineData("a b@example.com", false)] [InlineData(".a@example.com", false)] [InlineData("a..b@example.com", false)] [InlineData("a@-bad.com", false)]
    [InlineData("a@example..com", false)] [InlineData("a@example.com\n", false)]
    public void Email_shape_rules(string email, bool valid) => Assert.Equal(valid, AppUser.IsValidEmail(email));

    [Fact] public void An_email_longer_than_254_characters_is_invalid() => Assert.False(AppUser.IsValidEmail(new string('a', 60) + "@" + string.Join('.', Enumerable.Repeat(new string('b', 60), 4)) + ".com"));

    [Fact]
    public void Blank_optional_values_become_not_provided_and_nothing_is_defaulted()
    {
        Assert.Null(AppUser.NormalizeOptional("   ")); Assert.Null(AppUser.NormalizeOptional(null)); Assert.Equal("Planner", AppUser.NormalizeOptional("  Planner "));
        var u = AppUser.Create("someone", "Some One", UserRole.Operator, "hash", false, DateTimeOffset.UtcNow);
        Assert.Equal((null, null, null), (u.Email, u.Position, u.Department));
    }

    [Fact]
    public void Updating_the_own_profile_or_the_organization_never_touches_preferences_role_or_security_stamp()
    {
        var u = AppUser.Create("someone", "Some One", UserRole.Operator, "hash", false, DateTimeOffset.UtcNow);
        u.SetPreferences("dark", true); var stamp = u.SecurityStamp;
        u.UpdateOwnProfile("New Name", " me@example.com ", DateTimeOffset.UtcNow);
        u.SetOrganization(" Line lead ", "  ", DateTimeOffset.UtcNow);
        Assert.Equal(("New Name", "me@example.com", "Line lead", (string?)null), (u.DisplayName, u.Email, u.Position, u.Department));
        Assert.Equal(("dark", true, UserRole.Operator, stamp), (u.Theme, u.SidebarCollapsed, u.Role, u.SecurityStamp));
        Assert.Throws<DomainException>(() => u.UpdateOwnProfile("x", "not-an-email", DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => u.SetOrganization(new string('p', 101), null, DateTimeOffset.UtcNow));
    }
}
