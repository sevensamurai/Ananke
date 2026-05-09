namespace Ananke.Orchestration.Knowledge.Documents;

/// <summary>
/// A read-only stream wrapper that enforces a hard byte-cap on the total number of
/// bytes read from the underlying stream. Throws <see cref="InvalidOperationException"/>
/// as soon as a read would cause the cumulative byte count to exceed the cap.
/// </summary>
/// <remarks>
/// Used by <see cref="DocumentProcessor"/> to guarantee that document ingestion never
/// consumes more than <c>maxBytes</c> of memory regardless of whether <c>Content-Length</c>
/// was present or accurate in the HTTP response headers.
/// </remarks>
internal sealed class LimitedStream(Stream inner, long maxBytes, string sourceName) : Stream
{
    private long _bytesRead;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = inner.Read(buffer, offset, count);
        CheckLimit(n);
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int n = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        CheckLimit(n);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int n = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        CheckLimit(n);
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private void CheckLimit(int bytesJustRead)
    {
        _bytesRead += bytesJustRead;
        if (_bytesRead > maxBytes)
            throw new InvalidOperationException(
                $"Document '{sourceName}' exceeds the maximum ingestion size of {maxBytes:N0} bytes. " +
                "Increase DocumentProcessor.MaxContentLength or reduce the document size.");
    }
}
