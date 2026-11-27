using Xunit;
using PosApi.Services;

namespace PosApi.Tests;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Abcdef1!")]
    [InlineData("ValidP@ss1")]
    [InlineData("Secure#2026")]
    public void Validate_ValidPassword_ReturnsTrue(string password)
    {
        var (isValid, error) = PasswordPolicy.Validate(password);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Short1!")]
    [InlineData("abcdef1!")]
    [InlineData("ABCDEF1!")]
    [InlineData("Abcdef12")]
    public void Validate_InvalidPassword_ReturnsFalseWithErrorMessage(string? password)
    {
        var (isValid, error) = PasswordPolicy.Validate(password);
        Assert.False(isValid);
        Assert.NotNull(error);
    }
}
