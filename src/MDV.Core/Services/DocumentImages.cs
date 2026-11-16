using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MDV.Core.Services;

/// <summary>
/// 文書の相対パスの画像を、仮想ホスト http://document/ から返す(#17)。
/// 返すのは、文書のフォルダとその下にある画像ファイルだけ。それ以外の種類のファイルと、フォルダの外を指すパスには何も返さない。
/// (フォルダのマッピングは使わない。画像以外のファイルも読めるようになるため)
/// </summary>
public static class DocumentImages
{
    public const string Host = "document";
    public const string BaseUri = "http://document/";

    // 返してよい拡張子と Content-Type
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".bmp"] = "image/bmp",
        [".ico"] = "image/x-icon",
    };

    /// <summary>http://document/ の URI か(ページの移動先として扱わないために使う)</summary>
    public static bool IsDocumentUri(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttp && u.Host == Host;

    /// <summary>
    /// 要求された URI を、文書フォルダの中の画像ファイルの絶対パスにする。返してはいけないものは null。
    /// ここではパスの文字列だけを見る。リンクをたどった先の確認は <see cref="Read"/> で行う。
    /// </summary>
    public static string? ResolvePath(string docFolder, string requestUri)
    {
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var u) ||
            u.Scheme != Uri.UriSchemeHttp || u.Host != Host || !u.IsDefaultPort)
            return null;

        // Uri は "../" を取り除いて正規化する。残った %xx をデコードし、文書フォルダからの相対パスとして扱う
        var rel = Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/'));
        // \ (デコード後に現れたもの)、: (ドライブ名・代替データストリーム)、NUL は受け付けない
        if (rel.Length == 0 || rel.IndexOfAny(['\\', ':', '\0']) >= 0)
            return null;

        var root = FolderPrefix(Path.GetFullPath(docFolder));
        var full = Path.GetFullPath(Path.Combine(root, rel));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return null;
        return ContentTypes.ContainsKey(Path.GetExtension(full)) ? full : null;
    }

    /// <summary>
    /// 画像を読み込む。返してはいけないもの・読めないものは null。
    /// シンボリックリンクやジャンクションで文書フォルダの外を指していないかを、開いたファイルの実際のパスで確かめる。
    /// 8.3 形式の短い名前(例: SECRET~1.PNG が secret.pngx を指す)で別の種類のファイルを読まれないよう、拡張子も実際のパスで見直す
    /// (Path.GetFullPath も短い名前を長い名前に戻すが、それだけに頼らない)。
    /// </summary>
    public static (byte[] Data, string ContentType)? Read(string docFolder, string requestUri)
    {
        var path = ResolvePath(docFolder, requestUri);
        if (path == null) return null;

        try
        {
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var realFile = FinalPath(file);
            var realFolder = FinalFolderPath(docFolder);
            if (realFile == null || realFolder == null ||
                !realFile.StartsWith(FolderPrefix(realFolder), StringComparison.OrdinalIgnoreCase) ||
                !ContentTypes.TryGetValue(Path.GetExtension(realFile), out var type))
                return null;

            // ファイルを開いたままにしない(画像を編集・保存できなくならないように)、すぐに読み切って閉じる
            using var stream = new FileStream(file, FileAccess.Read);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return (buffer.ToArray(), type);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;   // 無い・フォルダ・読めないファイル
        }
    }

    // 末尾に区切りを 1 つだけ付ける(ドライブのルート "D:\" は元から付いている)
    private static string FolderPrefix(string folder) =>
        Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;

    // リンク(シンボリックリンク・ジャンクション)をすべてたどった先のパス
    private static string? FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        while (true)
        {
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
            if (length == 0) return null;
            if (length < buffer.Length) return new string(buffer, 0, (int)length);
            buffer = new char[length];
        }
    }

    private static string? FinalFolderPath(string folder)
    {
        // フォルダは File.OpenHandle で開けないため、FILE_FLAG_BACKUP_SEMANTICS を付けて CreateFileW で開く
        using var handle = CreateFileW(folder, FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        return handle.IsInvalid ? null : FinalPath(handle);
    }

    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x7;            // FILE_SHARE_READ | WRITE | DELETE
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] path, uint length, uint flags);
}
