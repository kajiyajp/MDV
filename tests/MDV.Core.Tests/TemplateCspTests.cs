using System.Text.RegularExpressions;

namespace MDV.Core.Tests;

/// <summary>
/// template.html の Content Security Policy を検査する(#17)。
/// 文書内のスクリプト・外部への通信・リモート画像を止めるためのもので、
/// テンプレートを書き換えたときに、うっかり緩めたり、インラインスクリプトの nonce を付け忘れたりしないようにする。
/// </summary>
public class TemplateCspTests
{
    private static readonly string Template = File.ReadAllText(
        Path.Combine(FindRepoRoot(), "src", "MDV.App", "Assets", "template.html"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MDV.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("MDV.sln が見つかりません");
    }

    private static string Policy()
    {
        var m = Regex.Match(Template, "<meta http-equiv=\"Content-Security-Policy\" content=\"([^\"]+)\">");
        Assert.True(m.Success, "CSP の meta が見つかりません");
        return m.Groups[1].Value;
    }

    private static string Directive(string name)
    {
        var d = Policy().Split(';').Select(s => s.Trim()).FirstOrDefault(s => s.StartsWith(name + " "));
        Assert.NotNull(d);
        return d!;
    }

    [Fact] public void Policy_is_before_the_body()
    {
        var policy = Template.IndexOf("Content-Security-Policy");
        Assert.True(policy >= 0 && policy < Template.IndexOf("{{BODY}}"));
    }

    [Fact] public void Policy_blocks_scripts_network_and_remote_images()
    {
        Assert.Equal("default-src 'none'", Directive("default-src"));
        Assert.Equal("connect-src 'none'", Directive("connect-src"));
        Assert.Equal("script-src http://vendor 'nonce-{{NONCE}}'", Directive("script-src"));
        Assert.Equal("img-src http://vendor http://document data:", Directive("img-src"));
        Assert.DoesNotContain("unsafe-eval", Policy());
    }

    // インラインスクリプトはすべて nonce 付き、外部スクリプトは同梱(http://vendor)のものだけ
    [Fact] public void Every_script_is_bundled_or_has_the_nonce()
    {
        var tags = Regex.Matches(Template, "<script[^>]*>").Select(m => m.Value).ToList();
        Assert.NotEmpty(tags);
        foreach (var tag in tags)
            Assert.True(tag.Contains("src=\"http://vendor/") || tag == "<script nonce=\"{{NONCE}}\">", tag);
    }

    // Replace はすべての出現箇所を置換するため、本文のプレースホルダは 1 か所だけにする(コメント内に書くと本文が二重に入る)
    [Fact] public void Body_placeholder_appears_once()
    {
        Assert.Single(Regex.Matches(Template, "\\{\\{BODY\\}\\}"));
    }
}
