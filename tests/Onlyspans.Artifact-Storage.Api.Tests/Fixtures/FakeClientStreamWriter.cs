using Grpc.Core;

namespace Onlyspans.Artifact_Storage.Api.Tests.Fixtures;

public sealed class FakeAsyncStreamReader<T> : IAsyncStreamReader<T>
{
    private readonly Queue<T> _messages;

    public FakeAsyncStreamReader(IEnumerable<T> messages)
    {
        _messages = new Queue<T>(messages);
    }

    public T Current { get; private set; } = default!;

    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        if (_messages.Count > 0)
        {
            Current = _messages.Dequeue();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}

public sealed class FakeServerStreamWriter<T> : IServerStreamWriter<T>
{
    private readonly List<T> _written = [];

    public IReadOnlyList<T> Written => _written;

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
    {
        _written.Add(message);
        return Task.CompletedTask;
    }

    public Task WriteAsync(T message, CancellationToken cancellationToken)
    {
        _written.Add(message);
        return Task.CompletedTask;
    }
}
