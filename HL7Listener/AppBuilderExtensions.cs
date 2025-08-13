namespace HL7Listener;
using Microsoft.AspNetCore.Connections;
using NHapi.Base.Parser;

/// <summary>
/// Extension methods for configuring HL7 TCP listener support in ASP.NET Core applications.
/// </summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Configures the ASP.NET Core application to listen for HL7 messages over TCP using the MLLP protocol.
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder instance to configure.</param>
    /// <param name="port">The TCP port number to listen on for HL7 messages.</param>
    /// <param name="enableTls">Whether to enable TLS/SSL encryption for the connection.</param>
    /// <param name="pfxCertificate">The path to the PFX certificate file (required when enableTls is true).</param>
    /// <param name="password">The password for the PFX certificate (required when enableTls is true).</param>
    /// <returns>The configured WebApplicationBuilder instance for method chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when enableTls is true but pfxCertificate or password is null or empty.</exception>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddSingleton&lt;PipeParser&gt;();
    /// builder.AddHL7Support(4040, false, null, null);
    /// </code>
    /// </example>
    public static WebApplicationBuilder AddHL7Support(  this WebApplicationBuilder builder,int port,bool enableTls,
                                                        string ? pfxCertificate,string ? password)
    {
        if ( enableTls)
        {
            ArgumentException.ThrowIfNullOrEmpty(pfxCertificate);
            ArgumentException.ThrowIfNullOrEmpty(password);
        }
        builder.WebHost.ConfigureKestrel(kestrelServerOptions =>
        {
            kestrelServerOptions.ListenAnyIP(port, listenOptions =>
            {
                if (enableTls)
                {
                    listenOptions.UseHttps(pfxCertificate, password);
                }
                ILoggerFactory loggerFactory = listenOptions.ApplicationServices.GetRequiredService<ILoggerFactory>();
                var hl7PipeParser = listenOptions.ApplicationServices.GetRequiredService<PipeParser>();
                listenOptions.Run(new HL7ConnectionHandler(hl7PipeParser,loggerFactory).HandleConnection);

            });

        });

        return builder;
    }
}
