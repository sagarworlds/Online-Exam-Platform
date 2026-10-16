namespace ExamPlatform.IntegrationTests;

/// <summary>The SMTP sink is a test double, but a test that never connects to it disposes it straight after starting it, so its shutdown must be quiet.</summary>
public class SmtpSinkTests
{
    // Stopping the listener while the accept loop is starting surfaces differently depending on which side wins: an InvalidOperationException
    // if the listener is already stopped, an ObjectDisposedException, or, on Linux, a SocketException ("Invalid argument") when the socket is
    // closed under the first accept. One start-and-stop is rarely enough to hit the last one, so the race is run many times.
    [Fact]
    public async Task DisposingStraightAfterStarting_NeverThrows_WhicheverWayTheStopRacesTheFirstAccept()
    {
        for (var i = 0; i < 3000; i++)
        {
            var sink = new SmtpSink();
            if (i % 3 == 1)
                await Task.Yield();
            else if (i % 3 == 2)
                Thread.SpinWait(200);

            await sink.DisposeAsync();
        }
    }
}
