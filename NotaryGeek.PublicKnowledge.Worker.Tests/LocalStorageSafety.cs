namespace NotaryGeek.PublicKnowledge.Worker.Tests;

internal static class LocalStorageSafety
{
    public static void RequireDisposableLoopback(string connection)
    {
        // Intentionally accept only the SDK's fixed loopback development account shorthand.
        // Reject suffixes, proxy overrides, arbitrary keys/endpoints and ambient cloud identities
        // before constructing clients or creating files/containers (including cleanup targets).
        if (!string.Equals(connection.Trim(), "UseDevelopmentStorage=true", StringComparison.Ordinal))
            throw new InvalidOperationException("Only the fixed disposable loopback development storage setting is permitted.");
        SyntheticBlobStore.RequireNoAmbientCloudIdentity();
    }
}

public sealed class LocalStorageSafetyTests
{
    [Theory]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=example;AccountKey=synthetic;EndpointSuffix=core.windows.net")]
    [InlineData("UseDevelopmentStorage=true;DevelopmentStorageProxyUri=https://example.invalid")]
    [InlineData("BlobEndpoint=http://localhost:10000/devstoreaccount1;AccountName=devstoreaccount1;AccountKey=synthetic")]
    [InlineData("")]
    public void ArbitraryConnectionsAreRejectedBeforeFixtureMutation(string connection) =>
        Assert.Throws<InvalidOperationException>(() => LocalStorageSafety.RequireDisposableLoopback(connection));
}
