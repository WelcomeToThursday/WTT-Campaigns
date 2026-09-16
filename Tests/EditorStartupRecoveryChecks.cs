using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Tests;

internal static class EditorStartupRecoveryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var rejection = new InvalidOperationException("Finish the raid before changing characters or modifiers.");
        var recovered = 0;
        Exception? reported = null;
        void Recover(Exception error)
        {
            recovered++;
            reported = error;
        }
        check(
            !EditorStartupRecovery.Prepare(true, () => throw rejection, Recover) && recovered == 1 && ReferenceEquals(reported, rejection),
            "Rejected automatic editor entry returns to normal backend and reports the original raid guard"
        );
        check(
            EditorStartupRecovery.Prepare(true, () => true, Recover) && recovered == 1,
            "Successful editor startup retains the editor backend"
        );
        check(
            !EditorStartupRecovery.Prepare(true, () => false, Recover) && recovered == 1,
            "Normal startup does not manufacture an editor failure"
        );
        var propagated = false;
        try
        {
            EditorStartupRecovery.Prepare(false, () => throw rejection, Recover);
        }
        catch (InvalidOperationException error)
        {
            propagated = ReferenceEquals(error, rejection);
        }
        check(propagated && recovered == 1, "An existing backend transition still fails closed instead of silently changing identity");
        check(
            !EditorStartupRecovery.Prepare(true, () => throw new IOException("Connection closed"), Recover)
                && recovered == 2
                && reported is IOException,
            "An editor startup transport failure also leaves normal backend authentication reachable"
        );
    }
}
