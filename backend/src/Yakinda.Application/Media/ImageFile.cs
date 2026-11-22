using System.Buffers.Binary;

namespace Yakinda.Application.Media;

public enum ImageFormat
{
    Jpeg,
    Png,
    WebP
}

/// <summary>
/// Yüklenen görsel. Biçim dosyanın kendi baytlarından anlaşılır (istemcinin gönderdiği Content-Type'a ya da
/// dosya adına güvenilmez). Kaydedilmeden önce gizlilik için meta veriler silinir: telefon fotoğraflarındaki
/// EXIF'te çekildiği yerin GPS koordinatı (çoğu zaman kişinin evi), cihaz modeli, tarih bulunur.
/// JPEG'in yön (orientation) bilgisi korunur ki dik çekilen fotoğraf yan dönmesin.
/// Çok büyük piksel boyutlu (sıkıştırılınca küçük ama açılınca telefonu kilitleyen) dosyalar reddedilir.
/// </summary>
public sealed class ImageFile
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxSide = 8000;
    public const long MaxPixels = 40_000_000;

    private ImageFile(byte[] data, ImageFormat format, int width, int height)
    {
        Data = data;
        Format = format;
        Width = width;
        Height = height;
    }

    /// <summary>Meta verisi silinmiş içerik.</summary>
    public byte[] Data { get; }

    public ImageFormat Format { get; }
    public int Width { get; }
    public int Height { get; }

    public string Extension => Format switch
    {
        ImageFormat.Jpeg => "jpg",
        ImageFormat.Png => "png",
        _ => "webp"
    };

    /// <summary>Geçerli bir JPEG, PNG ya da WebP değilse (ya da sınırları aşıyorsa) null.</summary>
    public static ImageFile? TryRead(byte[]? data)
    {
        if (data is null || data.Length < 16 || data.Length > MaxBytes)
            return null;

        if (Detect(data) is not { } format)
            return null;

        try
        {
            var result = format switch
            {
                ImageFormat.Jpeg => Jpeg.Sanitize(data),
                ImageFormat.Png => Png.Sanitize(data),
                _ => WebP.Sanitize(data)
            };

            if (result is not { } image)
                return null;
            var (clean, width, height) = image;
            if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide || (long)width * height > MaxPixels)
                return null;

            return new ImageFile(clean, format, width, height);
        }
        catch (ArgumentException)
        {
            // Bozuk uzunluk alanları dizinin dışına taşırsa (ArgumentOutOfRangeException dahil)
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static ImageFormat? Detect(ReadOnlySpan<byte> data)
    {
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ImageFormat.Jpeg;
        if (data[..8].SequenceEqual(Png.Signature))
            return ImageFormat.Png;
        if (data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return ImageFormat.WebP;
        return null;
    }

    // ------------------------------------------------------------------ JPEG

    private static class Jpeg
    {
        public static (byte[] Data, int Width, int Height)? Sanitize(byte[] src)
        {
            using var output = new MemoryStream(src.Length);
            output.Write(src, 0, 2); // SOI
            var pos = 2;
            int width = 0, height = 0;

            while (true)
            {
                if (pos + 1 >= src.Length || src[pos] != 0xFF)
                    return null;

                var marker = src[pos + 1];
                if (marker == 0xFF)
                {
                    pos++; // dolgu baytı
                    continue;
                }

                if (marker == 0xD9) // görüntü verisi (SOS) gelmeden dosya bitti
                    return null;

                if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) // uzunluksuz işaretler
                {
                    output.Write(src, pos, 2);
                    pos += 2;
                    continue;
                }

                if (pos + 3 >= src.Length)
                    return null;
                var length = (src[pos + 2] << 8) | src[pos + 3];
                if (length < 2 || pos + 2 + length > src.Length)
                    return null;
                var payload = src.AsSpan(pos + 4, length - 2);

                if (marker == 0xDA)
                {
                    // SOS: geri kalanı sıkıştırılmış görüntü verisi, olduğu gibi kopyalanır.
                    if (width == 0 || height == 0)
                        return null;
                    output.Write(src, pos, src.Length - pos);
                    return (output.ToArray(), width, height);
                }

                if (IsStartOfFrame(marker))
                {
                    if (payload.Length < 5)
                        return null;
                    height = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]);
                    width = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]);
                }

                if (marker == 0xE1)
                {
                    // APP1: EXIF (GPS, cihaz) ya da XMP. Yalnızca yön bilgisi yeni, küçük bir EXIF olarak yazılır.
                    var orientation = ReadOrientation(payload);
                    if (orientation is > 1 and <= 8)
                        output.Write(OrientationOnlyExif(orientation.Value));
                }
                else if (Keep(marker))
                {
                    output.Write(src, pos, 2 + length);
                }

                pos += 2 + length;
            }
        }

        /// <summary>
        /// Korunanlar: görüntüyü çözmek için gerekenler (tablolar, çerçeve), APP0 (JFIF), APP2 (ICC renk profili),
        /// APP14 (Adobe renk dönüşümü). Silinenler: diğer APPn (EXIF/XMP/IPTC/üretici verisi) ve COM (yorum).
        /// </summary>
        private static bool Keep(byte marker) => marker switch
        {
            0xE0 or 0xE2 or 0xEE => true,
            >= 0xE1 and <= 0xEF => false,
            0xFE => false,
            _ => true
        };

        private static bool IsStartOfFrame(byte marker) =>
            marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

        private static ushort? ReadOrientation(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 14 || !payload[..6].SequenceEqual("Exif\0\0"u8))
                return null;

            var tiff = payload[6..];
            bool little;
            if (tiff[0] == (byte)'I' && tiff[1] == (byte)'I') little = true;
            else if (tiff[0] == (byte)'M' && tiff[1] == (byte)'M') little = false;
            else return null;

            var ifd = U32(tiff[4..], little);
            if (ifd > (uint)tiff.Length - 2)
                return null;

            var count = U16(tiff[(int)ifd..], little);
            for (var i = 0; i < count; i++)
            {
                var entry = (int)ifd + 2 + i * 12;
                if (entry + 12 > tiff.Length)
                    break;
                if (U16(tiff[entry..], little) == 0x0112 && U16(tiff[(entry + 2)..], little) == 3)
                    return U16(tiff[(entry + 8)..], little);
            }

            return null;
        }

        private static byte[] OrientationOnlyExif(ushort orientation)
        {
            byte[] segment =
            [
                0xFF, 0xE1, 0x00, 0x22,                         // APP1, uzunluk 34
                (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
                (byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 0, 8,  // TIFF (big-endian), IFD0 ofseti 8
                0x00, 0x01,                                     // 1 kayıt
                0x01, 0x12, 0x00, 0x03, 0, 0, 0, 1,             // Orientation, SHORT, 1 adet
                0, (byte)orientation, 0, 0,                     // değer
                0, 0, 0, 0                                      // sonraki IFD yok
            ];
            return segment;
        }

        private static ushort U16(ReadOnlySpan<byte> s, bool little) =>
            little ? BinaryPrimitives.ReadUInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16BigEndian(s);

        private static uint U32(ReadOnlySpan<byte> s, bool little) =>
            little ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);
    }

    // ------------------------------------------------------------------ PNG

    private static class Png
    {
        public static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        /// <summary>Metin, zaman ve EXIF parçaları silinir; görüntü parçaları aynen kalır.</summary>
        private static readonly string[] Dropped = ["eXIf", "tEXt", "zTXt", "iTXt", "tIME"];

        public static (byte[] Data, int Width, int Height)? Sanitize(byte[] src)
        {
            using var output = new MemoryStream(src.Length);
            output.Write(src, 0, 8);
            long pos = 8;
            int width = 0, height = 0;
            var sawData = false;

            while (true)
            {
                if (pos + 12 > src.Length)
                    return null;

                long length = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan((int)pos));
                var type = System.Text.Encoding.ASCII.GetString(src, (int)pos + 4, 4);
                if (length > src.Length - pos - 12)
                    return null;

                if (pos == 8)
                {
                    if (type != "IHDR" || length < 8)
                        return null;
                    width = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan((int)pos + 8)), int.MaxValue);
                    height = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan((int)pos + 12)), int.MaxValue);
                }

                sawData |= type == "IDAT";
                if (!Dropped.Contains(type))
                    output.Write(src, (int)pos, (int)(12 + length));

                pos += 12 + length;
                if (type == "IEND")
                    return sawData ? (output.ToArray(), width, height) : null;
            }
        }
    }

    // ------------------------------------------------------------------ WebP

    private static class WebP
    {
        private const byte ExifFlag = 0x08;
        private const byte XmpFlag = 0x04;

        public static (byte[] Data, int Width, int Height)? Sanitize(byte[] src)
        {
            long riffEnd = 8L + BinaryPrimitives.ReadUInt32LittleEndian(src.AsSpan(4));
            if (riffEnd > src.Length)
                return null;

            using var chunks = new MemoryStream(src.Length);
            long pos = 12;
            int width = 0, height = 0;
            var sawImage = false;

            while (pos + 8 <= riffEnd)
            {
                var fourCc = System.Text.Encoding.ASCII.GetString(src, (int)pos, 4);
                long size = BinaryPrimitives.ReadUInt32LittleEndian(src.AsSpan((int)pos + 4));
                if (pos + 8 + size > riffEnd)
                    return null;
                var total = Math.Min(8 + size + (size & 1), riffEnd - pos);
                var data = src.AsSpan((int)pos + 8, (int)size);

                switch (fourCc)
                {
                    case "EXIF":
                    case "XMP ":
                        break; // silinir

                    case "VP8X":
                        if (size < 10)
                            return null;
                        width = 1 + (data[4] | (data[5] << 8) | (data[6] << 16));
                        height = 1 + (data[7] | (data[8] << 8) | (data[9] << 16));
                        var copy = src.AsSpan((int)pos, (int)total).ToArray();
                        copy[8] &= unchecked((byte)~(ExifFlag | XmpFlag));
                        chunks.Write(copy);
                        break;

                    case "VP8 ":
                        if (size < 10 || data[3] != 0x9D || data[4] != 0x01 || data[5] != 0x2A)
                            return null;
                        if (width == 0)
                        {
                            width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & 0x3FFF;
                            height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) & 0x3FFF;
                        }
                        sawImage = true;
                        chunks.Write(src, (int)pos, (int)total);
                        break;

                    case "VP8L":
                        if (size < 5 || data[0] != 0x2F)
                            return null;
                        if (width == 0)
                        {
                            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
                            width = (int)(bits & 0x3FFF) + 1;
                            height = (int)((bits >> 14) & 0x3FFF) + 1;
                        }
                        sawImage = true;
                        chunks.Write(src, (int)pos, (int)total);
                        break;

                    default:
                        sawImage |= fourCc == "ANMF"; // hareketli WebP kareleri
                        chunks.Write(src, (int)pos, (int)total);
                        break;
                }

                pos += total;
            }

            if (!sawImage)
                return null;

            var body = chunks.ToArray();
            var result = new byte[12 + body.Length];
            "RIFF"u8.CopyTo(result);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(4 + body.Length));
            "WEBP"u8.CopyTo(result.AsSpan(8));
            body.CopyTo(result, 12);
            return (result, width, height);
        }
    }
}
