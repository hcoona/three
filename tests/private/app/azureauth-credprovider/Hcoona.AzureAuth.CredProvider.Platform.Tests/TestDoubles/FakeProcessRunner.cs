using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;

public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<
        Func<ProcessStartSpec, CancellationToken, Task<ProcessResult>>
    > _handlers = [];

    public List<ProcessStartSpec> StartSpecs { get; } = [];

    public IReadOnlyList<ProcessStartSpec> RecordedStartSpecs => StartSpecs;

    public void EnqueueResult(ProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _handlers.Enqueue((_, _) => Task.FromResult(result));
    }

    public void EnqueueFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _handlers.Enqueue((_, _) => Task.FromException<ProcessResult>(exception));
    }

    public void EnqueueHandler(
        Func<ProcessStartSpec, CancellationToken, Task<ProcessResult>> handler
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handlers.Enqueue(handler);
    }

    public async Task<ProcessResult> RunAsync(
        ProcessStartSpec startSpec,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(startSpec);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateEnvironmentMode(startSpec.EnvironmentMode);

        StartSpecs.Add(startSpec);
        if (startSpec.PreStartValidation is not null)
        {
            await startSpec.PreStartValidation(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_handlers.Count == 0)
        {
            return new ProcessResult(0, string.Empty, string.Empty);
        }

        return await _handlers.Dequeue()(startSpec, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateEnvironmentMode(ProcessEnvironmentMode environmentMode)
    {
        if (
            environmentMode
            is not ProcessEnvironmentMode.Inherit
                and not ProcessEnvironmentMode.ExplicitOnly
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(environmentMode),
                environmentMode,
                "Unsupported process environment mode."
            );
        }
    }
}
