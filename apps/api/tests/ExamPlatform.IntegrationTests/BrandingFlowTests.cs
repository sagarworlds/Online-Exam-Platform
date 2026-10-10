using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The institute's branding over real HTTP and a real database (FR-41). A staff member sets the name, colour and logo the candidate pages
/// use; a logo is accepted only as an image of an allowed type and size; and the candidate pages read all of it without signing in.
/// Not run in this environment: the integration suite needs Docker, so these run in CI only.
/// </summary>
public sealed class BrandingFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // The start of a PNG file: its signature and the length and type of its first chunk. Enough for the format check, which reads only the signature.
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private static async Task<HttpResponseMessage> PutLogoAsync(HttpClient client, byte[] content, string mediaType = "image/png")
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/branding/logo") { Content = new ByteArrayContent(content) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task PutBranding_ThenRead_ReturnsTheSavedNameAndColour_WithoutSignIn()
    {
        using var admin = await factory.AdminClientAsync();

        (await admin.PutAsJsonAsync("/v1/branding", new { instituteName = "Riverside Academy", primaryColour = "#1a56db" })).EnsureSuccessStatusCode();

        using var anonymous = factory.CreateClient();
        var read = await JsonAsync((await anonymous.GetAsync("/v1/branding")).EnsureSuccessStatusCode());
        Assert.Equal("Riverside Academy", read.GetProperty("instituteName").GetString());
        Assert.Equal("#1A56DB", read.GetProperty("primaryColour").GetString());
    }

    [Fact]
    public async Task PutBranding_WithAColourThatIsNotSixDigitHex_Returns400_AndSavesNothing()
    {
        using var admin = await factory.AdminClientAsync();

        var response = await admin.PutAsJsonAsync("/v1/branding", new { instituteName = "Refused Name Test", primaryColour = "red" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_branding");
        using var anonymous = factory.CreateClient();
        var read = await JsonAsync(await anonymous.GetAsync("/v1/branding"));
        Assert.NotEqual("Refused Name Test", read.GetProperty("instituteName").GetString());
    }

    [Fact]
    public async Task PutBranding_AsACandidate_Returns403()
    {
        var (candidate, _) = await factory.CandidateClientAsync();

        var response = await candidate.PutAsJsonAsync("/v1/branding", new { instituteName = "Candidate Change" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutLogo_ThenRead_ServesTheSameBytesWithTheirMediaType_AndRevalidatesWith304()
    {
        using var admin = await factory.AdminClientAsync();
        (await PutLogoAsync(admin, PngBytes)).EnsureSuccessStatusCode();

        using var anonymous = factory.CreateClient();
        var logo = (await anonymous.GetAsync("/v1/branding/logo")).EnsureSuccessStatusCode();
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PngBytes, await logo.Content.ReadAsByteArrayAsync());
        var entityTag = logo.Headers.ETag?.Tag;
        Assert.NotNull(entityTag);

        // The browser sends the tag it was given back; an unchanged logo is then answered without its bytes.
        using var revalidate = new HttpRequestMessage(HttpMethod.Get, "/v1/branding/logo");
        revalidate.Headers.TryAddWithoutValidation("If-None-Match", entityTag);
        var notModified = await anonymous.SendAsync(revalidate);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    [Fact]
    public async Task PutLogo_OfAnUnsupportedImageType_Returns415()
    {
        using var admin = await factory.AdminClientAsync();
        var gif = Encoding.ASCII.GetBytes("GIF89a\u0001\u0000\u0001\u0000");

        // The declared media type is image/png, but the bytes are a GIF: the format is read from the bytes, so this is refused.
        var response = await PutLogoAsync(admin, gif, "image/png");

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "unsupported_logo_type");
    }

    [Fact]
    public async Task PutLogo_OverTheSizeLimit_Returns413()
    {
        using var admin = await factory.AdminClientAsync();
        var oversized = new byte[256 * 1024 + 1];
        PngBytes.CopyTo(oversized, 0);

        var response = await PutLogoAsync(admin, oversized);

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "logo_too_large");
    }

    [Fact]
    public async Task DeleteLogo_ThenReadingIt_Returns404()
    {
        using var admin = await factory.AdminClientAsync();
        (await PutLogoAsync(admin, PngBytes)).EnsureSuccessStatusCode();

        (await admin.DeleteAsync("/v1/branding/logo")).EnsureSuccessStatusCode();

        using var anonymous = factory.CreateClient();
        await AssertProblemAsync(await anonymous.GetAsync("/v1/branding/logo"), HttpStatusCode.NotFound, "logo_not_found");
    }

    [Fact]
    public async Task PutLogo_AsACandidate_Returns403()
    {
        var (candidate, _) = await factory.CandidateClientAsync();

        var response = await PutLogoAsync(candidate, PngBytes);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
