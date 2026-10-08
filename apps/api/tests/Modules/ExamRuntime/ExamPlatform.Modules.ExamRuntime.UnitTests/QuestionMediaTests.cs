using ExamPlatform.Modules.ExamRuntime.Application;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The pictures inside a question's text, taken out for a slow connection and found again when wanted (FR-53).</summary>
public class QuestionMediaTests
{
    private static readonly byte[] Small = [1, 2, 3, 4, 5];
    private static readonly byte[] Larger = Enumerable.Range(0, 300).Select(i => (byte)(i % 251)).ToArray();

    private static string Picture(byte[] bytes, string type = "png", string alt = "A diagram") =>
        $"<img src=\"data:image/{type};base64,{Convert.ToBase64String(bytes)}\" alt=\"{alt}\">";

    [Fact]
    public void Detach_TakesEachPictureOut_LeavingAMarkerThatNamesItAndSaysHowBigItIs()
    {
        var html = $"<p>Look at this</p>{Picture(Small)}<p>and this</p>{Picture(Larger, "jpeg", "Second")}";

        var detached = QuestionMedia.Detach(html);

        Assert.DoesNotContain("base64", detached);
        Assert.DoesNotContain("data:image", detached);
        Assert.Contains("class=\"lazy-media lazy-media--q-0 lazy-bytes--5\" alt=\"A diagram\"", detached);
        Assert.Contains("class=\"lazy-media lazy-media--q-1 lazy-bytes--300\" alt=\"Second\"", detached);
        Assert.StartsWith("<p>Look at this</p><img ", detached);
        Assert.Contains("<p>and this</p>", detached);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(10)]
    public void Detach_SaysTheSizeOfThePictureItselfNotOfItsEncoding(int length)
    {
        var detached = QuestionMedia.Detach(Picture(new byte[length]));

        Assert.Contains($"lazy-bytes--{length}\"", detached);
    }

    [Fact]
    public void Detach_LeavesATextWithNoPicturesExactlyAsItWas()
    {
        const string html = "<p>What is <strong>2 + 2</strong>?</p>";

        Assert.Same(html, QuestionMedia.Detach(html));
        Assert.Equal(string.Empty, QuestionMedia.Detach(string.Empty));
    }

    [Fact]
    public void Find_ReturnsEachPictureInTheOrderDetachNumberedThem()
    {
        var html = $"{Picture(Small)}<p>between</p>{Picture(Larger, "gif")}";

        var first = QuestionMedia.Find(html, 0);
        var second = QuestionMedia.Find(html, 1);

        Assert.Equal("image/png", first?.ContentType);
        Assert.Equal(Small, first?.Bytes);
        Assert.Equal("image/gif", second?.ContentType);
        Assert.Equal(Larger, second?.Bytes);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void Find_ReturnsNothingForAPictureTheTextDoesNotHave(int index)
    {
        Assert.Null(QuestionMedia.Find($"{Picture(Small)}{Picture(Small)}", index));
    }

    [Fact]
    public void Find_ReturnsNothingForATextWithNoPictures_OrDataThatIsNotValid()
    {
        Assert.Null(QuestionMedia.Find("<p>words</p>", 0));
        Assert.Null(QuestionMedia.Find(string.Empty, 0));
        Assert.Null(QuestionMedia.Find("<img src=\"data:image/png;base64,A\">", 0));
    }

    [Fact]
    public void AMarkersKeyFindsItsPictureInTheOriginalText()
    {
        var html = $"<p>x</p>{Picture(Small)}{Picture(Larger)}";
        var detached = QuestionMedia.Detach(html);

        Assert.Contains("lazy-media--q-1", detached);
        Assert.True(QuestionMedia.TryParseKey("q-1", out var index));
        Assert.Equal(Larger, QuestionMedia.Find(html, index)?.Bytes);
    }

    [Theory]
    [InlineData("q-0", 0)]
    [InlineData("q-12", 12)]
    public void TryParseKey_ReadsTheNumberOfAPicture(string key, int expected)
    {
        Assert.True(QuestionMedia.TryParseKey(key, out var index));
        Assert.Equal(expected, index);
    }

    [Theory]
    [InlineData("")]
    [InlineData("q")]
    [InlineData("q-")]
    [InlineData("q-x")]
    [InlineData("q--1")]
    [InlineData("q-99999999999")]
    [InlineData("-1")]
    [InlineData("o0123456789abcdef0123456789abcdef-0")]
    public void TryParseKey_RefusesAnythingElse(string key)
    {
        Assert.False(QuestionMedia.TryParseKey(key, out _));
    }
}
