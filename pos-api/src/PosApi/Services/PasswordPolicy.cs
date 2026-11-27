namespace PosApi.Services;

public static class PasswordPolicy
{
    public static (bool IsValid, string? Error) Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return (false, "Mật khẩu phải có độ dài tối thiểu 8 ký tự");
        if (!password.Any(char.IsUpper))
            return (false, "Mật khẩu phải chứa ít nhất một chữ hoa");
        if (!password.Any(char.IsLower))
            return (false, "Mật khẩu phải chứa ít nhất một chữ thường");
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            return (false, "Mật khẩu phải chứa ít nhất một ký tự đặc biệt");
        return (true, null);
    }
}
