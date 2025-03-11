using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Xml;

namespace MusicXml;

/// <summary>
/// MusicXMLのデシリアライズクラス
/// </summary>
public class MusicXmlDeserializer
{
    /// <summary>XMLデシリアライズ設定</summary>
    private static readonly XmlReaderSettings _xmlReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    /// <summary>取得対象とするMusicXMLのメディアタイプ</summary>
    private static readonly ImmutableHashSet<string> TargetMediaTypes = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
    [
        "application/vnd.recordare.musicxml",
        "application/vnd.recordare.musicxml+xml",
    ]);

    /// <summary>
    /// MusicXMLデータをパースする。
    /// </summary>
    /// <param name="stream">対象データ</param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    public static MusicXmlObject? Parse(Stream stream)
    {
        // 読み取り対象のストリーム
        bool isCopiedStream = false;

        // 現在位置
        long currentPosition = stream.Position;
        Span<byte> fileHeader = stackalloc byte[4];

        // ファイルヘッダーを読み込む
        if (stream.Read(fileHeader) < fileHeader.Length)
        {
            // 読み取れたデータが4バイト以下の場合
            // MusicXML関連のデータでない可能性が極めて高いので読み取りを諦める
            throw new NotSupportedException();
        }

        try
        {
            // ファイルヘッダの読み取り時に移動したファイルハンドルを元にに戻す
            // FileStream、MemoryStream等のシークできるストリームの場合はシーク位置を元に戻す
            // NetworkStreamなどのシークできないストリームの場合は一旦MemoryStreamに移す
            if (stream.CanSeek)
            {
                // FileStream、MemoryStream等のシークできるストリームの場合
                stream.Position = currentPosition;
            }
            else
            {
                // ネットワークストリームなどのシークできないストリームの場合
                // 読み取り済みのファイルヘッダとデータをMemoryStreamにコピーする
                var baseStream = stream;
                isCopiedStream = true;
                stream = new MemoryStream();
                stream.Write(fileHeader);
                stream.CopyTo(baseStream);
                stream.Position = 0;
            }

            if (IsZipHeader(fileHeader))
            {
                // ファイルの先頭がZIPヘッダなら圧縮済みMusicXMLとして処理する
                return ParseCompressedMusicXml(stream);
            }
            else
            {
                // その他の場合はMusicXMLとして処理する
                return ParseTextBaseMusicXml(stream);
            }
        }
        finally
        {
            // コピー済みストリームの場合は解放する
            if (isCopiedStream)
                stream.Dispose();
        }
    }

    /// <summary>
    /// 非圧縮のMusicXMLファイルをパースする。
    /// </summary>
    /// <param name="stream">対象データ</param>
    /// <returns></returns>
    public static MusicXmlObject? ParseTextBaseMusicXml(Stream stream)
        => ParseXml<MusicXmlObject>(stream);

    /// <summary>
    /// XMLデータをパースする。
    /// </summary>
    /// <typeparam name="T">デシリアライズ後の型</typeparam>
    /// <param name="stream">対象データ</param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    private static T ParseXml<T>(Stream stream) where T : class
    {
        using (var streamReader = new StreamReader(stream, MusicXmlHelper.TextEncode))
        using (var reader = XmlReader.Create(streamReader, _xmlReaderSettings))
            return MusicXmlHelper.GetXmlSerializer<T>().Deserialize(reader) as T ?? throw new NotSupportedException();
    }

    /// <summary>
    /// 圧縮済みMusicXMLをパースする。
    /// </summary>
    /// <param name="stream">対象データ</param>
    /// <returns></returns>
    private static MusicXmlObject? ParseCompressedMusicXml(Stream stream)
    {
        using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Read, true);

        // メタ情報(ファイル一覧)を取得
        if (TryGetArchiveFile(zipArchive, "META-INF/container.xml", out var filePath))
        {
            // MusicXMLを読み込む
            var scoreFile = zipArchive.GetEntry(filePath);
            if (scoreFile != null)
                using (var entryStream = scoreFile.Open())
                    return ParseTextBaseMusicXml(entryStream);
        }

        return null;
    }

    /// <summary>
    /// ZIPアーカイブからMusicXMLファイルを探す。
    /// </summary>
    /// <param name="zipArchive"></param>
    /// <param name="containerPath"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    private static bool TryGetArchiveFile(ZipArchive zipArchive, string containerPath, [NotNullWhen(true)] out string? path)
    {
        if (zipArchive.GetEntry(containerPath) is { } containerEntry)
        {
            // ZIPファイル内のMETA-INF/container.xmlを読み込む
            ContainerXmlObject? containerXml;
            using (var entryStream = containerEntry.Open())
                containerXml = ParseXml<ContainerXmlObject>(entryStream);

            // パスが設定されている情報に絞り込む
            var files = containerXml?.RootFiles?.RootFile?.Where(f => !string.IsNullOrEmpty(f.FullPath));
            if (files != null)
            {
                // MusicXMLファイルを探す
                // メディアタイプが設定されている場合はMusicXMLのメディアタイプに合致するものを優先する
                var file = files.FirstOrDefault(f => f.MediaType != null && TargetMediaTypes.Contains(f.MediaType))
                    ?? files.FirstOrDefault(f => f.FullPath!.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
                if (file != null)
                {
                    path = file.FullPath!;
                    return true;
                }
            }
        }

        path = null;
        return false;
    }

    /// <summary>
    /// ファイルヘッダがZIPかどうかを判定する。
    /// </summary>
    /// <param name="data">対象データ</param>
    /// <returns></returns>
    public static bool IsZipHeader(Span<byte> data)
        => data is [0x50, 0x4B, 0x03, 0x04, ..];
}
