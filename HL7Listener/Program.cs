using HL7Listener;
using NHapi.Base.Parser;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<PipeParser>();
builder.Services.AddSingleton<MessageStore>();

bool enableTls = false;
string certificateInPfx = "certificate.pfx";
string certificatePw = "PaSswoRd~@34";
var config = builder.Configuration;


int hl7Port = config.GetValue<int>("HL7Settings:HL7Port", 4040);
int webPort = config.GetValue<int>("HL7Settings:WebPort", 5000);

builder.AddHL7Support(hl7Port, enableTls, certificateInPfx, certificatePw);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(webPort);
});

//Build & Run
var app = builder.Build();

// API endpoint
app.MapGet("/api/stats", (MessageStore store) =>
{
    var (total, last, recent) = store.GetStats();
    return Results.Json(new { total, last, recent });
});

// Simple HTML dashboard
app.MapGet("/dashboard", () =>
{
    return Results.Content($@"
    <html>
    <head>
        <title>HL7 Dashboard</title>
        <style>
            body{{font-family:Segoe UI, sans-serif;background:#111;color:#eee;padding:20px}}
            pre{{background:#222;padding:10px;border-radius:8px;overflow-x:auto}}
        </style>
    </head>
    <body>
        <h1>HL7 Listener Dashboard</h1>
        <p><b>Total messages:</b> <span id='total'>&ndash;</span></p>
        <p><b>Last received:</b> <span id='last'>&ndash;</span></p>
        <h2>Recent Messages</h2>
        <div id='recent'><p><i>No messages received yet.</i></p></div>

        <script>
            async function refresh() {{
                try {{
                    const res = await fetch('/api/stats');
                    const data = await res.json();
                    document.getElementById('total').textContent = data.total;
                    document.getElementById('last').textContent =
                        data.last && data.last !== '0001-01-01T00:00:00'
                            ? new Date(data.last).toLocaleString()
                            : '-';

                    if (data.recent && data.recent.length > 0) {{
                        document.getElementById('recent').innerHTML =
                            data.recent.map(m => `<pre>${{m}}</pre>`).join('<hr/>');
                    }} else {{
                        document.getElementById('recent').innerHTML =
                            '<p><i>No messages received yet.</i></p>';
                    }}
                }} catch (err) {{
                    console.error('refresh failed', err);
                }}
            }}

            refresh();
            setInterval(refresh, 3000); // every 3 seconds
        </script>
    </body>
    </html>", "text/html");
});

app.Run();
