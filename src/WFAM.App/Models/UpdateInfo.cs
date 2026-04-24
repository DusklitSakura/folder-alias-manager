namespace WFAM.App.Models;

/// <summary>
/// GitHub Release 元数据 + 解析得到的版本号。
/// </summary>
public sealed record UpdateInfo(
    Version Version,        // 解析自 tag_name（去掉前缀 v）
    string TagName,         // 例如 "v1.2.0"
    string HtmlUrl,         // GitHub release 页面
    string? Body,           // 发行说明
    DateTimeOffset? PublishedAt,
    string? PrimaryAssetUrl // 第一个 .exe / .msi / .zip 资产下载地址（可空）
);
