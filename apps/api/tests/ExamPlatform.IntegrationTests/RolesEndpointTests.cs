using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.Identity.Domain.Rbac;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// <c>GET /v1/admin/roles</c> (FR-2): administrators who may assign roles can list what there is to
/// assign, and nobody else can.
/// </summary>
public sealed class RolesEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ListRoles_AsSuperAdmin_ReturnsEveryRoleWithItsPermissions()
    {
        using var client = AuthorizedClient((await factory.SignInAsAsync("SuperAdmin")).AccessToken);

        var response = await client.GetAsync("/v1/admin/roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var roles = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();

        // Alphabetical by name, so the list is stable for a UI.
        Assert.Equal(
            RbacCatalog.Roles.Select(r => r.Name).Order(StringComparer.Ordinal),
            roles.Select(r => r.GetProperty("name").GetString()!));

        foreach (var definition in RbacCatalog.Roles)
        {
            var role = roles.Single(r => r.GetProperty("name").GetString() == definition.Name);
            Assert.NotEqual(Guid.Empty, role.GetProperty("id").GetGuid());
            Assert.Equal(definition.RequiresTwoFactor, role.GetProperty("requiresTwoFactor").GetBoolean());
            Assert.Equal(
                definition.PermissionCodes.Order(StringComparer.Ordinal),
                role.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!));
        }
    }

    [Theory]
    [InlineData("ExamAdmin")]
    [InlineData("InstituteTeacher")]
    [InlineData("Candidate")]
    public async Task ListRoles_WithoutTheRoleAssignPermission_Returns403(string roleName)
    {
        using var client = AuthorizedClient((await factory.SignInAsAsync(roleName)).AccessToken);

        var response = await client.GetAsync("/v1/admin/roles");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListRoles_WithoutAuth_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/admin/roles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListedRoleId_CanBeUsedToAssignThatRole()
    {
        using var client = AuthorizedClient((await factory.SignInAsAsync("SuperAdmin")).AccessToken);
        var target = await factory.SignInAsAsync("Candidate");

        var roles = (await client.GetFromJsonAsync<JsonElement>("/v1/admin/roles")).EnumerateArray();
        var examAdminId = roles.Single(r => r.GetProperty("name").GetString() == "ExamAdmin").GetProperty("id").GetGuid();

        var assign = await client.PostAsJsonAsync($"/v1/admin/users/{target.UserId}/roles", new { roleId = examAdminId });

        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
    }

    private HttpClient AuthorizedClient(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
