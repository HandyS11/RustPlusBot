using System.Globalization;

namespace RustPlusBot.Features.Pairing.Rendering;

/// <summary>Custom ids for the server-pairing prompt in #setup. Tails encode "{ip}:{port}".</summary>
internal static class ServerPairingComponentIds
{
    /// <summary>Prompt Accept button; tail "{ip}:{port}".</summary>
    public const string AcceptPrefix = "server:accept:";

    /// <summary>Prompt Dismiss button; tail "{ip}:{port}".</summary>
    public const string DismissPrefix = "server:dismiss:";

    /// <summary>Builds the "{ip}:{port}" custom-id tail for an endpoint.</summary>
    /// <param name="ip">The server host or ip.</param>
    /// <param name="port">The Rust+ app port.</param>
    /// <returns>The custom-id tail.</returns>
    public static string Tail(string ip, int port) =>
        ip + ":" + port.ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses an "{ip}:{port}" tail. Splits on the last ':' so IPv6 hosts keep their colons.</summary>
    /// <param name="tail">The custom-id tail.</param>
    /// <param name="ip">The parsed host or ip.</param>
    /// <param name="port">The parsed port (1-65535).</param>
    /// <returns>True when the tail carried a well-formed endpoint.</returns>
    public static bool TryParseTail(string? tail, out string ip, out int port)
    {
        ip = string.Empty;
        port = 0;
        if (string.IsNullOrEmpty(tail))
        {
            return false;
        }

        var sep = tail.LastIndexOf(':');
        if (sep <= 0 || sep == tail.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(tail[(sep + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port)
            || port is < 1 or > 65535)
        {
            return false;
        }

        ip = tail[..sep];
        return true;
    }
}
