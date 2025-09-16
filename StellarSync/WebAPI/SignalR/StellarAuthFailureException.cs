namespace StellarSync.WebAPI.SignalR;

public class StellarAuthFailureException : Exception
{
    public StellarAuthFailureException(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}