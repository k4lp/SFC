namespace SalesforceCore.Http;

/// <summary>
/// 2.7.0: read-only wrapper that throws once more than a set number of bytes has been read. Used to cap decompressed
/// gzip responses so a small compressed body cannot expand without limit (decompression bomb).
/// </summary>
public sealed class SizeLimitedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private long _read;

    /// <summary>Wraps <paramref name="inner"/>; <paramref name="limit"/> &lt;= 0 means unlimited.</summary>
    public SizeLimitedReadStream(Stream inner, long limit)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limit = limit;
    }

    private int Count(int n)
    {
        _read += n;
        if (_limit > 0 && _read > _limit)
        {
            throw new InvalidDataException(
                $"Salesforce response exceeded the {_limit:N0}-byte decompressed size limit (SalesforceOptions.MaxDecompressedResponseBytes).");
        }
        return n;
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => Count(_inner.Read(buffer, offset, count));
    /// <inheritdoc/>
    public override int Read(Span<byte> buffer) => Count(_inner.Read(buffer));
    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => Count(await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));
    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanSeek => false;
    /// <inheritdoc/>
    public override bool CanWrite => false;
    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();
    /// <inheritdoc/>
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    /// <inheritdoc/>
    public override void Flush() { }
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
