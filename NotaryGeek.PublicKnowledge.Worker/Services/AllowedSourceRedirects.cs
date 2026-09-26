using System.Net;

namespace NotaryGeek.PublicKnowledge.Worker.Services;

// Only the two named source clients use this policy. Provider clients retain their own HTTP behavior.
public static class AllowedSourceRedirects
{
    public const int MaxRedirects = 5;

    public static bool TryValidate(string url, string allowedSourceHosts, out Uri? uri, out string reason)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
            !uri.IsWellFormedOriginalString() ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            uri = null;
            reason = "Invalid source URL.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            uri = null;
            reason = "Only HTTPS URLs are allowed.";
            return false;
        }

        if (!allowedSourceHosts.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            uri = null;
            reason = "Source host is not allowlisted.";
            return false;
        }

        reason = "allowed";
        return true;
    }

    public static async Task<(HttpResponseMessage Response, Uri FinalUri)> SendAsync(
        HttpClient client, Uri originalUri, string allowedSourceHosts, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = originalUri;
        for (var hops = 0; ; hops++)
        {
            if (!TryValidate(current.AbsoluteUri, allowedSourceHosts, out _, out var reason))
            {
                throw new SourceRedirectException(reason);
            }

            if (!visited.Add(current.AbsoluteUri))
            {
                throw new SourceRedirectException("Source redirect loop detected.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
            {
                return (response, current);
            }

            try
            {
                // Read the raw header so a malformed Location cannot be mistaken for no redirect.
                if (!response.Headers.TryGetValues("Location", out var values) ||
                    values.Count() != 1 || string.IsNullOrWhiteSpace(values.Single()) ||
                    !Uri.TryCreate(current, values.Single(), out var target) ||
                    !TryValidate(target.AbsoluteUri, allowedSourceHosts, out _, out reason))
                {
                    throw new SourceRedirectException(
                        reason == "allowed" ? "Invalid source redirect location." : reason);
                }

                if (hops >= MaxRedirects)
                {
                    throw new SourceRedirectException("Source redirect limit exceeded.");
                }

                current = target;
            }
            finally
            {
                response.Dispose();
            }
        }
    }
}

public sealed class SourceRedirectException(string message) : Exception(message);
