using Microsoft.AspNetCore.Connections;

namespace HL7Listener
{
    public static class HL7Extensions
    {
        public static void AddHL7Support(
            this WebApplicationBuilder builder,
            int port,
            bool enableTls,
            string? pfxCertificate = null,
            string? password = null)
        {
            // Validate TLS settings if enabled
            if (enableTls)
            {
                ArgumentException.ThrowIfNullOrEmpty(pfxCertificate);
                ArgumentException.ThrowIfNullOrEmpty(password);
            }

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(port, listenOptions =>
                {
                    if (enableTls)
                    {
                        listenOptions.UseHttps(pfxCertificate!, password!);
                    }

                    // Use DI to resolve HL7ConnectionHandler automatically
                    listenOptions.UseConnectionHandler<HL7ConnectionHandler>();
                });
            });
        }
    }
}
