using FaturamentoService.Services;
using Xunit;

namespace Korp.IntegrationTests;

public class AuthenticationAttemptGuardTests
{
    [Fact]
    public void FifthFailure_BlocksFurtherAttempts()
    {
        var guard = new AuthenticationAttemptGuard();

        for (var attempt = 1; attempt <= 4; attempt++) Assert.False(guard.RegisterFailure("login:client:user"));
        Assert.True(guard.RegisterFailure("login:client:user"));
        Assert.True(guard.IsBlocked("login:client:user"));
    }

    [Fact]
    public void Clear_RemovesPreviousFailures()
    {
        var guard = new AuthenticationAttemptGuard();
        for (var attempt = 1; attempt <= 4; attempt++) guard.RegisterFailure("login:client:user");

        guard.Clear("login:client:user");

        Assert.False(guard.RegisterFailure("login:client:user"));
        Assert.False(guard.IsBlocked("login:client:user"));
    }

    [Theory]
    [InlineData("JBSWY3DPEHPK3PXP", true)]
    [InlineData("segredo-invalido", false)]
    [InlineData("CURTO", false)]
    public void TotpSecret_RequiresValidBase32(string secret, bool expected) => Assert.Equal(expected, Totp.IsValidSecret(secret));
}
