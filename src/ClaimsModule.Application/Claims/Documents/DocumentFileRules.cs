using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ClaimsModule.Domain.Exceptions;

namespace ClaimsModule.Application.Claims.Documents;

public sealed record ValidatedDocumentFile(string FileName, string ContentType, long SizeBytes, string Sha256);

public static class DocumentFileRules
{
    public const long MaxBytes = 50L * 1024 * 1024;
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff];
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv"
    };

    public static async Task<ValidatedDocumentFile> ValidateAsync(string fileName, string? suppliedContentType,
        Stream content, CancellationToken cancellationToken)
    {
        if (!content.CanSeek || content.Length is <= 0 or > MaxBytes)
        {
            throw new ClaimRuleViolation("File", "Upload a nonempty file of up to 50 MiB.");
        }

        var leaf = fileName.Replace('\\', '/').Split('/')[^1];
        var extension = Path.GetExtension(leaf).ToLowerInvariant();
        var mime = suppliedContentType?.Split(';', 2)[0].Trim();
        if (!ContentTypes.TryGetValue(extension, out var expectedType) ||
            (!string.Equals(mime, expectedType, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(mime, "application/octet-stream", StringComparison.OrdinalIgnoreCase) &&
             !(extension == ".csv" && string.Equals(mime, "application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase))))
        {
            throw new ClaimRuleViolation("File", "Upload a PDF, JPEG, PNG, DOCX, XLSX, TXT or CSV with a matching media type.");
        }

        try
        {
            content.Position = 0;
            var header = new byte[Math.Min(8, (int)content.Length)];
            await content.ReadExactlyAsync(header, cancellationToken);
            var matches = extension switch
            {
                ".pdf" => header.AsSpan().StartsWith("%PDF-"u8),
                ".jpg" or ".jpeg" => header.AsSpan().StartsWith(Jpeg),
                ".png" => header.AsSpan().StartsWith(Png),
                ".docx" => header.AsSpan().StartsWith("PK\u0003\u0004"u8) && IsOfficeFile(content, "word/document.xml"u8),
                ".xlsx" => header.AsSpan().StartsWith("PK\u0003\u0004"u8) && IsOfficeFile(content, "xl/workbook.xml"u8),
                _ => await IsTextAsync(content, cancellationToken)
            };
            if (!matches)
            {
                throw new ClaimRuleViolation("File", "The file contents do not match the selected document type.");
            }

            var stem = new string(Path.GetFileNameWithoutExtension(leaf)
                .Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').Take(100).ToArray());
            if (string.IsNullOrWhiteSpace(stem))
            {
                stem = "document";
            }

            content.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken));
            return new ValidatedDocumentFile(stem + extension, expectedType, content.Length, hash);
        }
        finally
        {
            content.Position = 0;
        }
    }

    private static bool IsOfficeFile(Stream content, ReadOnlySpan<byte> part)
    {
        // Read a bounded ZIP directory, without decompressing the attachment.
        var tail = new byte[Math.Min(65_557, (int)content.Length)];
        content.Position = content.Length - tail.Length;
        content.ReadExactly(tail);
        for (var offset = tail.Length - 22; offset >= 0; offset--)
        {
            var end = tail.AsSpan(offset);
            if (BinaryPrimitives.ReadUInt32LittleEndian(end) != 0x06054b50 ||
                BinaryPrimitives.ReadUInt16LittleEndian(end[20..]) != end.Length - 22)
            {
                continue;
            }

            var count = BinaryPrimitives.ReadUInt16LittleEndian(end[10..]);
            var directoryBytes = BinaryPrimitives.ReadUInt32LittleEndian(end[12..]);
            var directoryStart = BinaryPrimitives.ReadUInt32LittleEndian(end[16..]);
            if (BinaryPrimitives.ReadUInt32LittleEndian(end[4..]) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(end[8..]) != count || count is 0 or > 4096 ||
                directoryBytes > 2 * 1024 * 1024 ||
                (long)directoryStart + directoryBytes != content.Length - end.Length)
            {
                return false;
            }

            var directory = new byte[(int)directoryBytes];
            content.Position = directoryStart;
            content.ReadExactly(directory);
            return HasOfficeParts(content, directoryStart, directory, count, part);
        }

        return false;
    }

    private static bool HasOfficeParts(Stream content, long directoryStart, ReadOnlySpan<byte> directory, int count,
        ReadOnlySpan<byte> part)
    {
        var hasContentTypes = false;
        var hasPart = false;
        for (var i = 0; i < count; i++)
        {
            const int headerLength = 46;
            if (directory.Length < headerLength || BinaryPrimitives.ReadUInt32LittleEndian(directory) != 0x02014b50)
            {
                return false;
            }

            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(directory[28..]);
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(directory[30..]);
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(directory[32..]);
            var length = headerLength + nameLength + extraLength + commentLength;
            if (length > directory.Length)
            {
                return false;
            }

            var name = directory.Slice(headerLength, nameLength);
            var isContentTypes = name.SequenceEqual("[Content_Types].xml"u8);
            var isPart = name.SequenceEqual(part);
            if ((isContentTypes && hasContentTypes) || (isPart && hasPart) ||
                ((isContentTypes || isPart) && !HasLocalPart(content, directoryStart, directory, name)))
            {
                return false;
            }

            hasContentTypes |= isContentTypes;
            hasPart |= isPart;
            directory = directory[length..];
        }

        return directory.IsEmpty && hasContentTypes && hasPart;
    }

    private static bool HasLocalPart(Stream content, long directoryStart, ReadOnlySpan<byte> central, ReadOnlySpan<byte> name)
    {
        const int headerLength = 30;
        long offset = BinaryPrimitives.ReadUInt32LittleEndian(central[42..]);
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(central[8..]);
        var method = BinaryPrimitives.ReadUInt16LittleEndian(central[10..]);
        var compressed = BinaryPrimitives.ReadUInt32LittleEndian(central[20..]);
        var uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(central[24..]);
        if (offset + headerLength > directoryStart || (flags & 0x41) != 0 || method is not (0 or 8) ||
            compressed is 0 or uint.MaxValue || uncompressed is 0 or uint.MaxValue)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[headerLength];
        content.Position = offset;
        content.ReadExactly(header);
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[26..]);
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x04034b50 ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != flags ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[8..]) != method || nameLength != name.Length ||
            offset + headerLength + nameLength + extraLength + compressed > directoryStart ||
            ((flags & 8) == 0 && !header.Slice(14, 12).SequenceEqual(central.Slice(16, 12))))
        {
            return false;
        }

        Span<byte> localName = stackalloc byte[name.Length];
        content.ReadExactly(localName);
        return localName.SequenceEqual(name);
    }

    private static async Task<bool> IsTextAsync(Stream content, CancellationToken cancellationToken)
    {
        content.Position = 0;
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == '\uFFFD' || char.IsControl(buffer[i]) && buffer[i] is not ('\r' or '\n' or '\t'))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
