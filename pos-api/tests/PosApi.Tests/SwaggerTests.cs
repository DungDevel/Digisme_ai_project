using Xunit;

namespace PosApi.Tests;

public class SwaggerTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public SwaggerTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SwaggerJson_CanBeGenerated_Successfully()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var content = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Swagger JSON failed with status {response.StatusCode}: {content}");
    }
}
