using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Orbiq.Services;

/// <summary>
/// Проверяет пользовательские аудиофайлы по содержимому, а не только по расширению.
/// Orbiq никогда не запускает импортированные файлы как программы.
/// </summary>
public static class MediaAssetValidator
{
    public const long MaximumAudioFileSize = 30L * 1024 * 1024;

    private static readonly byte[] AsfHeader =
    {
        0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
        0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
    };
    private static readonly byte[] AsfAudioMediaType =
    {
        0x40, 0x9E, 0x69, 0xF8, 0x4D, 0x5B, 0xCF, 0x11,
        0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B
    };

    /// <returns>Пустая строка при успехе; понятное сообщение при ошибке.</returns>
    public static string ValidateAudioFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return "Файл не найден. Выбери существующий аудиофайл.";

        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not ".wav" and not ".mp3" and not ".wma")
            return "Поддерживаются только WAV, MP3 и WMA.";

        try
        {
            var info = new FileInfo(path);
            if (info.Length < 128)
                return "Файл слишком маленький и не похож на полноценную аудиозапись.";
            if (info.Length > MaximumAudioFileSize)
                return "Размер аудиофайла не должен превышать 30 МБ.";

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            bool valid = extension switch
            {
                ".wav" => IsValidPcmWave(reader, stream.Length),
                ".mp3" => IsValidMp3(reader, stream.Length),
                ".wma" => IsValidAsf(reader),
                _ => false
            };

            return valid
                ? string.Empty
                : "Содержимое файла не соответствует формату WAV, MP3 или WMA. Возможно, это другой файл с изменённым расширением.";
        }
        catch (UnauthorizedAccessException)
        {
            return "Нет доступа к выбранному файлу.";
        }
        catch (IOException)
        {
            return "Не удалось прочитать файл. Возможно, он занят другой программой.";
        }
        catch
        {
            return "Не удалось проверить этот файл. Выбери другой аудиофайл.";
        }
    }

    private static bool IsValidPcmWave(BinaryReader reader, long fileLength)
    {
        Stream stream = reader.BaseStream;
        if (fileLength < 44) return false;

        stream.Position = 0;
        if (ReadFourCc(reader) != "RIFF") return false;
        uint riffSize = reader.ReadUInt32();
        if (ReadFourCc(reader) != "WAVE") return false;
        long riffEnd = Math.Min(fileLength, 8L + riffSize);
        if (riffEnd < 44) return false;

        bool foundFormat = false;
        bool foundData = false;
        stream.Position = 12;

        while (stream.Position + 8 <= riffEnd)
        {
            string chunkName = ReadFourCc(reader);
            uint chunkSize = reader.ReadUInt32();
            long chunkStart = stream.Position;
            long nextChunk = chunkStart + chunkSize + (chunkSize & 1);
            if (nextChunk > riffEnd || nextChunk > fileLength) return false;

            if (chunkName == "fmt ")
            {
                if (chunkSize < 16) return false;
                ushort format = reader.ReadUInt16();
                ushort channels = reader.ReadUInt16();
                uint sampleRate = reader.ReadUInt32();
                _ = reader.ReadUInt32(); // byte rate
                ushort blockAlign = reader.ReadUInt16();
                ushort bitsPerSample = reader.ReadUInt16();
                bool supportedEncoding = format is 1 or 3; // PCM или IEEE float, без сжатых кодеков.
                bool supportedChannels = channels is >= 1 and <= 8;
                bool supportedRate = sampleRate is >= 8000 and <= 192000;
                bool supportedBits = bitsPerSample is 8 or 16 or 24 or 32;
                foundFormat = supportedEncoding && supportedChannels && supportedRate && supportedBits && blockAlign > 0;
            }
            else if (chunkName == "data")
            {
                foundData = chunkSize > 0;
            }

            if (foundFormat && foundData) return true;
            stream.Position = nextChunk;
        }

        return false;
    }

    private static bool IsValidMp3(BinaryReader reader, long fileLength)
    {
        Stream stream = reader.BaseStream;
        long start = 0;
        stream.Position = 0;

        if (fileLength >= 10)
        {
            byte[] prefix = reader.ReadBytes(10);
            if (prefix.Length == 10 && prefix[0] == (byte)'I' && prefix[1] == (byte)'D' && prefix[2] == (byte)'3')
            {
                if (prefix[3] == 0xFF || prefix[3] == 0 || prefix[4] == 0xFF) return false;
                if ((prefix[6] | prefix[7] | prefix[8] | prefix[9]) >= 0x80) return false;
                int tagSize = (prefix[6] << 21) | (prefix[7] << 14) | (prefix[8] << 7) | prefix[9];
                start = 10L + tagSize + ((prefix[5] & 0x10) != 0 ? 10 : 0);
                if (start + 4 >= fileLength) return false;
            }
        }

        long scanEnd = Math.Min(fileLength - 4, start + 65536);
        for (long offset = start; offset <= scanEnd; offset++)
        {
            stream.Position = offset;
            uint header = ((uint)reader.ReadByte() << 24) |
                          ((uint)reader.ReadByte() << 16) |
                          ((uint)reader.ReadByte() << 8) |
                          reader.ReadByte();

            if ((header & 0xFFE00000u) != 0xFFE00000u) continue;
            int version = (int)((header >> 19) & 0x3);
            int layer = (int)((header >> 17) & 0x3);
            int bitrateIndex = (int)((header >> 12) & 0xF);
            int sampleRateIndex = (int)((header >> 10) & 0x3);
            if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || sampleRateIndex == 3) continue;

            // Проверяем, что найден не случайный набор битов, а следующий кадр MP3.
            int[] mpeg1Layer3Bitrates = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
            int[] mpeg2Layer3Bitrates = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };
            int[] sampleRates = { 44100, 48000, 32000 };
            int sampleRate = sampleRates[sampleRateIndex] / (version == 3 ? 1 : version == 2 ? 2 : 4);
            int bitrate = (version == 3 ? mpeg1Layer3Bitrates : mpeg2Layer3Bitrates)[bitrateIndex] * 1000;
            int padding = (int)((header >> 9) & 1);
            int frameLength = (version == 3 ? 144 : 72) * bitrate / sampleRate + padding;
            long nextFrame = offset + frameLength;
            if (frameLength < 24 || nextFrame + 4 > fileLength) continue;

            stream.Position = nextFrame;
            uint nextHeader = ((uint)reader.ReadByte() << 24) |
                              ((uint)reader.ReadByte() << 16) |
                              ((uint)reader.ReadByte() << 8) |
                              reader.ReadByte();
            if ((nextHeader & 0xFFE00000u) == 0xFFE00000u &&
                ((nextHeader >> 19) & 0x3) != 1 && ((nextHeader >> 17) & 0x3) == 1)
                return true;
        }

        return false;
    }

    private static bool IsValidAsf(BinaryReader reader)
    {
        Stream stream = reader.BaseStream;
        if (stream.Length < 100) return false;
        stream.Position = 0;
        byte[] header = reader.ReadBytes(16);
        if (header.Length != 16 || !header.SequenceEqual(AsfHeader)) return false;

        // WMA должен содержать объект типа Audio Media, а не просто ASF-видеоконтейнер.
        stream.Position = 0;
        byte[] scan = reader.ReadBytes((int)Math.Min(stream.Length, 1024 * 1024));
        for (int i = 30; i <= scan.Length - AsfAudioMediaType.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < AsfAudioMediaType.Length; j++)
            {
                if (scan[i + j] == AsfAudioMediaType[j]) continue;
                match = false;
                break;
            }
            if (match) return true;
        }
        return false;
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        return bytes.Length == 4 ? Encoding.ASCII.GetString(bytes) : string.Empty;
    }
}
