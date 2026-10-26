using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class ProxyAddressTests
{
    [Theory]
    [InlineData("127.0.0.1:7890", "127.0.0.1", 7890)]
    [InlineData("http://127.0.0.1:7890", "127.0.0.1", 7890)]
    [InlineData(" https://localhost:1080/ ", "localhost", 1080)]
    [InlineData("10.0.0.5:1", "10.0.0.5", 1)]
    [InlineData("proxy.example.com:65535", "proxy.example.com", 65535)]
    public void Parse_AcceptsValidAddress(string address, string expectedHost, int expectedPort)
    {
        Assert.True(NetworkClientProvider.TryParseProxyAddress(address, out var host, out var port));
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:0")]
    [InlineData("127.0.0.1:65536")]
    [InlineData(":7890")]
    [InlineData("127.0.0.1:abc")]
    [InlineData("http://")]
    public void Parse_RejectsInvalidAddress(string? address)
    {
        Assert.False(NetworkClientProvider.TryParseProxyAddress(address, out _, out _));
    }
}
