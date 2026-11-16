using System.Diagnostics;
using MDV.Core.Services;
using Xunit;

/// <summary>
/// 文書の相対パスの画像(http://document/)で返してよい範囲を検査する(#17)。
/// 返すのは文書のフォルダとその下の画像ファイルだけで、それ以外の種類・フォルダの外は何も返さない。
/// </summary>
public class DocumentImagesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mdv-images-" + Guid.NewGuid().ToString("N"));
    private readonly string _doc;
    private readonly string _outside;

    public DocumentImagesTests()
    {
        _doc = Path.Combine(_root, "doc");
        _outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(Path.Combine(_doc, "sub"));
        Directory.CreateDirectory(Path.Combine(_doc, "folder.png"));
        Directory.CreateDirectory(_outside);
        File.WriteAllBytes(Path.Combine(_doc, "a.png"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_doc, "B.JPG"), [4]);
        File.WriteAllBytes(Path.Combine(_doc, "sub", "c.svg"), [5]);
        File.WriteAllBytes(Path.Combine(_doc, "sub", "한글 이름.webp"), [6]);
        File.WriteAllText(Path.Combine(_doc, "note.md"), "secret");
        File.WriteAllText(Path.Combine(_doc, "secret.txt"), "secret");
        File.WriteAllBytes(Path.Combine(_outside, "out.png"), [7]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private (byte[] Data, string ContentType)? Read(string uri) => DocumentImages.Read(_doc, uri);

    [Theory]
    [InlineData("http://document/a.png", "image/png", new byte[] { 1, 2, 3 })]
    [InlineData("http://document/B.JPG", "image/jpeg", new byte[] { 4 })]
    [InlineData("http://document/sub/c.svg", "image/svg+xml", new byte[] { 5 })]
    [InlineData("http://document/sub/%ED%95%9C%EA%B8%80%20%EC%9D%B4%EB%A6%84.webp", "image/webp", new byte[] { 6 })]
    [InlineData("http://document/a.png?v=1#x", "image/png", new byte[] { 1, 2, 3 })]
    public void Images_in_the_folder_and_subfolders_are_returned(string uri, string type, byte[] data)
    {
        var image = Read(uri);
        Assert.NotNull(image);
        Assert.Equal(type, image!.Value.ContentType);
        Assert.Equal(data, image.Value.Data);
    }

    // 画像以外の種類は、フォルダの中にあっても返さない
    [Theory]
    [InlineData("http://document/note.md")]
    [InlineData("http://document/secret.txt")]
    [InlineData("http://document/secret.txt%00.png")]
    [InlineData("http://document/secret.txt:x.png")]
    [InlineData("http://document/")]
    [InlineData("http://document/folder.png")]       // 画像の名前のフォルダ
    [InlineData("http://document/missing.png")]      // 無いファイル
    public void Other_files_are_not_returned(string uri) => Assert.Null(Read(uri));

    // フォルダの外は返さない(文書フォルダの隣に outside/out.png がある)
    [Theory]
    [InlineData("http://document/../outside/out.png")]
    [InlineData("http://document/%2e%2e/outside/out.png")]
    [InlineData("http://document/%2E%2E%2Foutside%2Fout.png")]
    [InlineData("http://document/..%5Coutside%5Cout.png")]
    [InlineData("http://document/sub/../../outside/out.png")]
    public void Paths_outside_the_folder_are_not_returned(string uri) => Assert.Null(Read(uri));

    // 実際のパスでの確認(Read)より前の、パスの文字列だけでの判定でも外を指すものを落とす
    [Theory]
    [InlineData("http://document/%2E%2E%2Foutside%2Fout.png")]
    [InlineData("http://document/..%5Coutside%5Cout.png")]
    [InlineData("http://document/C:/x.png")]
    [InlineData("http://document/a.png:x")]
    public void ResolvePath_rejects_paths_outside_the_folder(string uri) =>
        Assert.Null(DocumentImages.ResolvePath(_doc, uri));

    // ドライブのルートにある文書(D:\README.md)でも、その下の画像を指せる
    [Fact]
    public void A_document_at_the_drive_root_can_use_images() =>
        Assert.Equal(@"D:\img\a.png", DocumentImages.ResolvePath(@"D:\", "http://document/img/a.png"));

    // Uri が "../" を取り除くので、外を指すつもりのパスもフォルダの中のパスになる(そこに無ければ返さない)
    [Fact]
    public void Dot_segments_stay_inside_the_folder() =>
        Assert.Equal(Path.Combine(_doc, "outside", "out.png"),
            DocumentImages.ResolvePath(_doc, "http://document/../outside/out.png"));

    [Fact]
    public void Absolute_paths_are_not_returned()
    {
        var outside = Path.Combine(_outside, "out.png");
        Assert.Null(Read("http://document/" + outside.Replace('\\', '/')));                // C:/.../out.png
        Assert.Null(Read("http://document/" + Uri.EscapeDataString(outside)));              // C%3A%5C...
        Assert.Null(Read("http://document//" + _outside.Replace('\\', '/') + "/out.png"));
    }

    [Theory]
    [InlineData("https://document/a.png")]
    [InlineData("http://document:8080/a.png")]
    [InlineData("http://vendor/a.png")]
    [InlineData("file:///C:/a.png")]
    public void Other_hosts_are_not_returned(string uri) => Assert.Null(Read(uri));

    // フォルダの中のジャンクションが外を指していても、たどった先が外なら返さない
    [Fact]
    public void Junctions_that_point_outside_are_not_followed()
    {
        var link = Path.Combine(_doc, "link");
        if (!MakeJunction(link, _outside)) return;   // ジャンクションを作れない環境では確かめられない
        Assert.True(File.Exists(Path.Combine(link, "out.png")));
        Assert.Null(Read("http://document/link/out.png"));
    }

    // 8.3 形式の短い名前は画像の拡張子に見えることがある(secret.pngx → SECRET~1.PNG)。実際の名前の拡張子で判定する
    [Fact]
    public void Short_names_do_not_turn_other_files_into_images()
    {
        var file = Path.Combine(_doc, "secret-long-name.pngx");
        File.WriteAllText(file, "secret");
        var shortName = Path.GetFileName(ShortPath(file));
        if (!shortName.EndsWith(".PNG", StringComparison.OrdinalIgnoreCase)) return;   // 短い名前を作らないドライブでは確かめられない
        Assert.Null(Read("http://document/" + shortName));
    }

    // 文書フォルダ自体をジャンクション経由で開いた場合は、その中の画像を返す
    [Fact]
    public void A_document_folder_opened_through_a_junction_still_works()
    {
        var link = Path.Combine(_root, "doc-link");
        if (!MakeJunction(link, _doc)) return;
        Assert.NotNull(DocumentImages.Read(link, "http://document/a.png"));
    }

    [Theory]
    [InlineData("http://document/a.png", true)]
    [InlineData("http://document/other.md#x", true)]
    [InlineData("https://document/a.png", false)]
    [InlineData("http://vendor/x.js", false)]
    [InlineData("https://example.com/", false)]
    [InlineData(null, false)]
    public void IsDocumentUri_matches_only_the_document_host(string? uri, bool expected) =>
        Assert.Equal(expected, DocumentImages.IsDocumentUri(uri));

    private static string ShortPath(string path)
    {
        var buffer = new char[1024];
        var length = GetShortPathNameW(path, buffer, (uint)buffer.Length);
        return length == 0 ? path : new string(buffer, 0, (int)length);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint GetShortPathNameW(string longPath, [System.Runtime.InteropServices.Out] char[] shortPath, uint length);

    private static bool MakeJunction(string link, string target)
    {
        var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        p.WaitForExit();
        return p.ExitCode == 0 && Directory.Exists(link);
    }
}
