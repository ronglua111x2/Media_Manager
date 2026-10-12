using System.Net.Http;
using System.Security.Authentication;

namespace media_management_app.Common;

public static class SslTlsErrorDetector
{
    public static bool IsSslOrTlsError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return true;
            }

            if (current is IOException or HttpRequestException)
            {
                var message = current.Message;
                if (message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("TLS", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("secure channel", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("schannel", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
