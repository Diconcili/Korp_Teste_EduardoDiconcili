using BillingTokens = FaturamentoService.Services.SessionTokenService;
using EstoqueService.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Korp.IntegrationTests;

public class RequestAuthTests
{
    const string SigningKey = "chave-compartilhada-para-assinar-sessoes-korp";
    const string ServiceKey = "chave-interna-entre-os-servicos-korp-teste";

    [Fact]
    public void UserToken_WhenSignedAndActive_IsAcceptedByStockService()
    {
        var token = new BillingTokens(SigningKey).Create(DateTime.UtcNow.AddMinutes(5));
        var request = RequestWithHeader("Authorization", $"Bearer {token}");

        Assert.True(new RequestAuth(SigningKey, ServiceKey).HasValidUserToken(request));
    }

    [Fact]
    public void UserToken_WhenTampered_IsRejectedByStockService()
    {
        var token = new BillingTokens(SigningKey).Create(DateTime.UtcNow.AddMinutes(5));
        var replacement = token[^1] == '0' ? '1' : '0';
        var request = RequestWithHeader("Authorization", $"Bearer {token[..^1]}{replacement}");

        Assert.False(new RequestAuth(SigningKey, ServiceKey).HasValidUserToken(request));
    }

    [Fact]
    public void UserToken_WhenExpired_IsRejectedByStockService()
    {
        var token = new BillingTokens(SigningKey).Create(DateTime.UtcNow.AddSeconds(-1));
        var request = RequestWithHeader("Authorization", $"Bearer {token}");

        Assert.False(new RequestAuth(SigningKey, ServiceKey).HasValidUserToken(request));
    }

    [Fact]
    public void ServiceKey_OnlyAcceptsConfiguredCredential()
    {
        var auth = new RequestAuth(SigningKey, ServiceKey);

        Assert.True(auth.HasValidServiceKey(RequestWithHeader("X-Korp-Service-Key", ServiceKey)));
        Assert.False(auth.HasValidServiceKey(RequestWithHeader("X-Korp-Service-Key", "credencial-incorreta")));
    }

    static HttpRequest RequestWithHeader(string name, string value)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[name] = value;
        return context.Request;
    }
}
