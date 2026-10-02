using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Infrastructure;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>
/// The sanitizer is the only thing between an author's markup and every candidate's browser, so these tests are a table
/// of known script-injection tricks that must all come out harmless, plus proof that real formatting survives.
/// </summary>
public class RichTextSanitizerTests
{
    private readonly RichTextSanitizer sanitizer = new();

    /// <summary>A real 1x1 PNG, as a browser would embed it.</summary>
    internal const string TinyPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static string DataUri(string mime, int decodedBytes) =>
        $"data:{mime};base64,{Convert.ToBase64String(new byte[decodedBytes])}";

    /// <summary>Anything in the output that a browser could act on.</summary>
    private static readonly string[] Dangerous =
    [
        "<script", "onerror", "onload", "onclick", "onmouseover", "javascript:", "vbscript:", "<iframe", "<svg", "<style", "<img",
        "<object", "<embed", "<form", "<input", "<meta", "<base", "<link", "<a ", "href=", "style=", "srcdoc", "data:",
    ];

    public static TheoryData<string> Attacks => new()
    {
        "<script>alert(1)</script>",
        "<SCRIPT SRC=https://evil.example/x.js></SCRIPT>",
        "<scr<script>ipt>alert(1)</scr</script>ipt>",
        "<img src=x onerror=alert(1)>",
        "<img src=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">",
        "<a href=\"javascript:alert(1)\">click</a>",
        "<a href=\" jav&#x09;ascript:alert(1)\">click</a>",
        "<p onclick=\"alert(1)\">text</p>",
        "<p style=\"background:url(javascript:alert(1))\">text</p>",
        "<svg onload=alert(1)>",
        "<svg><script>alert(1)</script></svg>",
        "<iframe src=\"https://evil.example\"></iframe>",
        "<iframe srcdoc=\"<script>alert(1)</script>\"></iframe>",
        "<style>body{display:none}</style>",
        "<object data=\"https://evil.example/x.swf\"></object>",
        "<embed src=\"https://evil.example/x.swf\">",
        "<form action=\"https://evil.example\"><input name=password></form>",
        "<meta http-equiv=\"refresh\" content=\"0;url=https://evil.example\">",
        "<base href=\"https://evil.example/\">",
        "<link rel=\"stylesheet\" href=\"https://evil.example/x.css\">",
        "<math><mtext></p><script>alert(1)</script></mtext></math>",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
        "<details open ontoggle=alert(1)>x</details>",
    };

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Sanitize_RemovesWhatABrowserCouldExecute(string attack)
    {
        var result = sanitizer.Sanitize(attack);

        foreach (var marker in Dangerous)
        {
            Assert.DoesNotContain(marker, result.Html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Sanitize_KeepsAnEmbeddedPictureAndItsDescription()
    {
        var result = sanitizer.Sanitize($"<p>Look: <img src=\"{TinyPng}\" alt=\"a dot\"></p>");

        Assert.Equal(1, result.ImageCount);
        Assert.Equal(0, result.RejectedImageCount);
        Assert.Contains($"src=\"{TinyPng}\"", result.Html);
        Assert.Contains("alt=\"a dot\"", result.Html);
        Assert.True(result.HasContent);
    }

    [Fact]
    public void Sanitize_OfAPictureAlone_HasContent()
    {
        var result = sanitizer.Sanitize($"<p><img src=\"{TinyPng}\"></p>");

        Assert.True(result.HasContent);
        Assert.Equal(string.Empty, result.PlainText);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    public void Sanitize_AcceptsEachAllowedPictureType(string mime) =>
        Assert.Equal(1, sanitizer.Sanitize($"<img src=\"{DataUri(mime, 100)}\">").ImageCount);

    [Fact]
    public void Sanitize_AcceptsAPictureAtTheSizeLimit_AndARealisticOne()
    {
        // The library must cope with the length of a real embedded picture, not only toy ones.
        Assert.Equal(1, sanitizer.Sanitize($"<img src=\"{DataUri("image/jpeg", 300 * 1024)}\">").ImageCount);
        Assert.Equal(1, sanitizer.Sanitize($"<img src=\"{DataUri("image/png", Question.MaxImageBytes)}\">").ImageCount);
    }

    [Fact]
    public void Sanitize_RefusesAPictureOverTheSizeLimit()
    {
        var result = sanitizer.Sanitize($"<p>x</p><img src=\"{DataUri("image/png", Question.MaxImageBytes + 1)}\">");

        Assert.Equal(0, result.ImageCount);
        Assert.Equal(1, result.RejectedImageCount);
        Assert.DoesNotContain("<img", result.Html);
    }

    [Theory]
    [InlineData("https://example.com/cat.png")]
    [InlineData("//example.com/cat.png")]
    [InlineData("/relative/cat.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+")]
    [InlineData("data:image/svg+xml,%3Csvg onload=alert(1)%3E")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("data:image/png;base64,not*valid*base64!")]
    [InlineData("data:image/png,rawbytes")]
    [InlineData("")]
    public void Sanitize_RefusesAnyPictureSourceThatIsNotAnEmbeddedPicture_AndSaysSo(string source)
    {
        var result = sanitizer.Sanitize($"<p>text</p><img src=\"{source}\" alt=\"x\">");

        Assert.Equal(0, result.ImageCount);
        Assert.Equal(1, result.RejectedImageCount);
        Assert.DoesNotContain("<img", result.Html);
        Assert.DoesNotContain(source.Length == 0 ? "\u0000" : source, result.Html);
    }

    [Fact]
    public void Sanitize_StripsEverythingFromAPictureExceptItsSourceAndDescription()
    {
        var result = sanitizer.Sanitize($"<img src=\"{TinyPng}\" alt=\"d\" onerror=\"alert(1)\" onload=\"alert(2)\" style=\"x\" width=\"9\" srcset=\"https://e.example/a.png 2x\" class=\"c\">");

        Assert.Equal(1, result.ImageCount);
        Assert.DoesNotContain("onerror", result.Html);
        Assert.DoesNotContain("onload", result.Html);
        Assert.DoesNotContain("style=", result.Html);
        Assert.DoesNotContain("width=", result.Html);
        Assert.DoesNotContain("srcset", result.Html);
        Assert.DoesNotContain("class=", result.Html);
    }

    [Fact]
    public void Sanitize_DropsEmptyParagraphsAtTheEnd_ButKeepsOnesInTheMiddle()
    {
        Assert.Equal("<p>text</p>", sanitizer.Sanitize("<p>text</p><p></p><p><br></p><p>  </p>").Html);
        Assert.Equal("<p>one</p><p></p><p>two</p>", sanitizer.Sanitize("<p>one</p><p></p><p>two</p>").Html);
        Assert.Equal($"<img src=\"{TinyPng}\">", sanitizer.Sanitize($"<img src=\"{TinyPng}\"><p></p>").Html);
        Assert.False(sanitizer.Sanitize("<p></p><p><br></p>").HasContent);
    }

    [Fact]
    public void Sanitize_DoesNotMistakeAPictureOnlyParagraphForAnEmptyOne() =>
        Assert.Contains("<img", sanitizer.Sanitize($"<p>x</p><p><img src=\"{TinyPng}\"></p>").Html);

    [Fact]
    public void Sanitize_KeepsFormattingAQuestionNeeds()
    {
        var result = sanitizer.Sanitize(
            "<p>Water is H<sub>2</sub>O and x<sup>2</sup> is <strong>big</strong>, <em>really</em>, <u>very</u>.</p>" +
            "<ul><li>one</li><li>two</li></ul><ol><li>first</li></ol><blockquote>quote</blockquote><pre><code>x = 1</code></pre>");

        Assert.Contains("H<sub>2</sub>O", result.Html);
        Assert.Contains("x<sup>2</sup>", result.Html);
        Assert.Contains("<strong>big</strong>", result.Html);
        Assert.Contains("<em>really</em>", result.Html);
        Assert.Contains("<u>very</u>", result.Html);
        Assert.Contains("<ul><li>one</li><li>two</li></ul>", result.Html);
        Assert.Contains("<ol><li>first</li></ol>", result.Html);
        Assert.Contains("<blockquote>quote</blockquote>", result.Html);
        Assert.Contains("<pre><code>x = 1</code></pre>", result.Html);
    }

    [Fact]
    public void Sanitize_DropsEveryAttributeFromAnAllowedTag() =>
        Assert.Equal("<p>hi</p>", sanitizer.Sanitize("<p class=\"x\" id=\"y\" style=\"color:red\" data-a=\"1\" title=\"t\">hi</p>").Html);

    [Fact]
    public void Sanitize_KeepsTheTextOfATagItDoesNotAllow()
    {
        var result = sanitizer.Sanitize("<div>hello <span>world</span></div>");

        Assert.Equal("hello world", result.PlainText);
        Assert.DoesNotContain("<div", result.Html);
        Assert.DoesNotContain("<span", result.Html);
    }

    [Fact]
    public void Sanitize_KeepsLiteralAngleBracketsAsText()
    {
        var result = sanitizer.Sanitize("<p>if a &lt; b &amp;&amp; b &gt; c</p>");

        Assert.Equal("if a < b && b > c", result.PlainText);
        Assert.Contains("&lt;", result.Html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    [InlineData("<p><br></p>")]
    [InlineData("<p>   </p>")]
    [InlineData("<script>alert(1)</script>")]
    public void Sanitize_OfNothingVisible_HasNoContent(string? html) =>
        Assert.False(sanitizer.Sanitize(html).HasContent);

    [Fact]
    public void Sanitize_OfPlainText_HasContentAndReportsItsText()
    {
        var result = sanitizer.Sanitize("What is 2 + 2?");

        Assert.True(result.HasContent);
        Assert.Equal("What is 2 + 2?", result.PlainText);
        Assert.Equal(0, result.ImageCount);
    }
}
