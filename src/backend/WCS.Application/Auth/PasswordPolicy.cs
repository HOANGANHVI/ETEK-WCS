using WCS.Application.Config;

namespace WCS.Application.Auth;

/// <summary>
/// Minimum password rules. NEEDS_CONFIRMATION: the customer has not specified a password policy; these are conservative technical defaults.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    public static IReadOnlyList<FieldError> Check(string? password, string username, string field = "password")
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrEmpty(password)) { errors.Add(new FieldError(field, "Cần nhập mật khẩu.")); return errors; }
        if (password.Length < MinLength) errors.Add(new FieldError(field, $"Mật khẩu phải có ít nhất {MinLength} ký tự."));
        if (password.Length > MaxLength) errors.Add(new FieldError(field, $"Mật khẩu không được dài quá {MaxLength} ký tự."));
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) errors.Add(new FieldError(field, "Mật khẩu phải gồm cả chữ và số."));
        if (!string.IsNullOrEmpty(username) && password.Contains(username, StringComparison.OrdinalIgnoreCase))
            errors.Add(new FieldError(field, "Mật khẩu không được chứa tên đăng nhập."));
        return errors;
    }

    public static void Require(string? password, string username, string field = "password")
    {
        var errors = Check(password, username, field);
        if (errors.Count > 0) throw new ValidationFailedException(errors);
    }
}
