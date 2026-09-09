namespace TheBleedingDeacons.Intergroup.Link.Models
{
    public class BetterStackConfiguration
    {
        private string _endpoint = string.Empty;

        public string SourceToken { get; set; } = string.Empty;

        /// <summary>
        /// Better Stack's HTTP ingest endpoint.
        /// </summary>
        /// <remarks>
        /// <para>Normalised on the way in: a value with no scheme gets
        /// <c>https://</c>. Better Stack's dashboard shows the ingest address as a
        /// bare hostname — <c>sNNNNNN.eu-central-1a.betterstackdata.com</c> — so
        /// that is what gets pasted into configuration, but <see cref="IsValid"/>
        /// requires an absolute HTTPS URI and <c>Uri.TryCreate</c> refuses a
        /// bare hostname.</para>
        ///
        /// <para>Without this the configuration reads as invalid, the logger
        /// controller takes its "config invalid or cleared" branch, and the app
        /// ships no logs at all — silently, because having no sink configured is a
        /// legitimate state. Register carried exactly that bug: its dev builds
        /// never shipped a log line to Better Stack, and nothing ever said so.</para>
        ///
        /// <para>Normalising in the setter rather than at the point of use is
        /// deliberate: the value arrives from an embedded JSON file and from the
        /// settings page, and a fix applied to only some paths would leave the
        /// same trap for the next one added.</para>
        /// </remarks>
        public string Endpoint
        {
            get => _endpoint;
            set => _endpoint = NormaliseEndpoint(value);
        }

        /// <summary>
        /// Validates the Better Stack configuration.
        /// </summary>
        /// <remarks>
        /// HTTPS only. The endpoint is editable and BetterStackHttpClient
        /// attaches the source token as a Bearer header on every batch, so an
        /// http:// address would ship that token -- and whatever the logs
        /// contain -- in the clear. A bare hostname is still accepted: the
        /// setter gives it https:// on the way in.
        /// </remarks>
        /// <returns>True if configuration is valid, false otherwise.</returns>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(SourceToken) &&
                   !string.IsNullOrWhiteSpace(Endpoint) &&
                   Uri.TryCreate(Endpoint, UriKind.Absolute, out var parsed) &&
                   parsed.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>
        /// Creates a copy with sensitive fields masked for logging.
        /// </summary>
        public BetterStackConfiguration ToLogSafe()
        {
            return new BetterStackConfiguration
            {
                SourceToken = string.IsNullOrEmpty(SourceToken) ? "" : "***",
                Endpoint = Endpoint
            };
        }

        /// <summary>
        /// Gives a scheme-less endpoint the <c>https://</c> it needs to parse as
        /// an absolute URI. An empty or whitespace value stays empty — that means
        /// "not configured", which is a supported state and must not become a bare
        /// "https://".
        /// </summary>
        private static string NormaliseEndpoint(string? value)
        {
            var endpoint = (value ?? string.Empty).Trim();

            if (endpoint.Length == 0)
            {
                return string.Empty;
            }

            // Checking for "://" rather than a known scheme prefix keeps an
            // explicit http:// working unchanged, and avoids mangling anything
            // that already carries a scheme we don't recognise.
            return endpoint.Contains("://", StringComparison.Ordinal)
                ? endpoint
                : "https://" + endpoint;
        }
    }
}
