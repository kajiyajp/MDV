using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDV.Core.Models;

/// <summary>永続化するアプリ設定: テーマ/文字倍率/ステータスバー表示/表示言語</summary>
public class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public double ZoomFactor { get; set; } = 1.0;
    public bool ShowStatusBar { get; set; } = true;
    /// <summary>UIの表示言語(BCP-47タグ)。空文字はWindowsの表示言語に合わせる</summary>
    public string Language { get; set; } = "";
    /// <summary>外部のリンク(ブラウザで開く)と、文書のフォルダの外にある文書を開く前に確認するか。既定は確認する</summary>
    public bool ConfirmExternalOpen { get; set; } = true;

    // JSON変換はソース生成(AppSettingsJsonContext)で行う。Release構成(PublishTrimmed)では
    // リフレクションによるJSON変換が無効になり、Serialize(this) が例外になって設定が保存されないため。
    public string ToJson() => JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings);

    public static AppSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AppSettings();
        try { return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
    }
}

[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
