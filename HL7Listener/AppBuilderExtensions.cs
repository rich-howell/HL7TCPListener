namespace HL7Listener;
using Microsoft.AspNetCore.Connections;
using NHapi.Base.Parser;

public static class AppBuilderExtensions
{
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
            kestrelServerOptions.ListenAnyIP(4040, listenOptions =>
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
