using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using NHapi.Base.Parser;
using NHapiTools.Base;
using NHapiTools.Base.Util;
using System.Buffers;
using System.Text;

namespace HL7Listener;

public class HL7ConnectionHandler : ConnectionHandler
{
    private readonly ILogger _logger;
    private readonly PipeParser pipeParser;
    private readonly IConfiguration config;
    private readonly ILoggerFactory loggerFactory;
    private readonly MessageStore store;
    private readonly string DumpFolder;
    private readonly SseHub hub;

    public HL7ConnectionHandler(
        PipeParser pipeParser,
        ILoggerFactory loggerFactory,
        IConfiguration config,
        MessageStore store,
        SseHub hub
        )
    {
        _logger = loggerFactory.CreateLogger<HL7ConnectionHandler>();
        this.pipeParser = pipeParser;
        this.loggerFactory = loggerFactory;
        this.config = config;
        this.store = store;
        this.hub = hub;

        DumpFolder = config["HL7Settings:DumpFolder"]
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ReceivedHL7");

        Directory.CreateDirectory(DumpFolder);
    }

    public override async Task OnConnectedAsync(ConnectionContext connectionContext)
    {
        // call your existing logic that handles a TCP connection
        await HandleConnection(connectionContext);
    }

    public async Task HandleConnection(ConnectionContext connectionContext)
    {
        var remote = connectionContext.RemoteEndPoint?.ToString() ?? "Unknown";

        Directory.CreateDirectory(DumpFolder);

        _logger.LogInformation("New connection established from {Remote}", remote);

        var startTime = DateTime.UtcNow;

        try
        {
            while (true)
            {
                var result = await connectionContext.Transport.Input.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (buffer.Length > 0)
                {
                    _logger.LogDebug("Received {Bytes} bytes from {Remote}", buffer.Length, remote);
                }

                while (TryReadMllpFrame(ref buffer, out ReadOnlySequence<byte> frame))
                {
                    _logger.LogDebug("Processing MLLP frame ({Length} bytes) from {Remote}", frame.Length, remote);                    
                    await ProcessHl7Message(frame, connectionContext.Transport);
                }

                connectionContext.Transport.Input.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    _logger.LogInformation("Connection from {Remote} closed by client", remote);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during connection with {Remote}", remote);
        }
        finally
        {
            var elapsed = DateTime.UtcNow - startTime;
            _logger.LogInformation("Disconnected from {Remote} (session length {Seconds}s)", remote, elapsed.TotalSeconds);
            await connectionContext.Transport.Input.CompleteAsync();
        }

    }

    private static string ConsoleSafe(string hl7)
    {
        return hl7
            .Replace(((char)0x0B).ToString(), "")          // strip VT
            .Replace(((char)0x1C).ToString(), "")          // strip FS
            .Replace("\r", Environment.NewLine)            // make segments visible
            .TrimEnd();                                     // (optionally .TrimEnd() to remove trailing extra newline)
    }

    private bool TryReadMllpFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> frame)
    {
        var start = buffer.PositionOf((byte)0x0B); // VT
        var end = buffer.PositionOf((byte)0x1C);   // FS

        if (start != null && end != null)
        {
            // slice cleanly between start and end
            frame = buffer.Slice(start.Value, buffer.GetPosition(1, end.Value));

            // advance to the byte after the FS (not two steps!)
            buffer = buffer.Slice(buffer.GetPosition(1, end.Value));

            return true;
        }

        frame = default;
        return false;
    }

    private async Task ProcessHl7Message(ReadOnlySequence<byte> message,System.IO.Pipelines.IDuplexPipe transport)
    {
        // Flatten the sequence into one contiguous byte array
        var data = message.ToArray();

        int start = Array.IndexOf(data, (byte)0x0B); //(VT = 0x0B
        int end = Array.IndexOf(data, (byte)0x1C); //FS = 0x1C

        byte[] payload = data;
        if(start >= 0 && end > start)
        {
            payload = data.Skip(start + 1).Take(end - start - 1).ToArray();
        }

        var strMessage = Encoding.UTF8.GetString(payload);

        _logger.LogInformation("Received raw HL7 message:\n{Message}", ConsoleSafe(strMessage));

        try
        {
            var parsedMessage = pipeParser.Parse(strMessage);
            var terser = new NHapi.Base.Util.Terser(parsedMessage);

            var controlId = terser.Get("/MSH-10") ?? "Unknown";
            var msgCode = terser.Get("/MSH-9-1") ?? "Unknown";
            var trigger = terser.Get("/MSH-9-2") ?? "Unknown";
            var msgType = $"{msgCode}^{trigger}";
            var sendingApp = terser.Get("/MSH-3") ?? "UnknownApp";
            var sendingFacility = terser.Get("/MSH-4") ?? "UnknownFacility";

            _logger.LogInformation("Parsed HL7 message {ControlId}: {MsgType} from {App}/{Facility}",controlId, msgType, sendingApp, sendingFacility);

            var filename = $"{DateTime.Now:yyyyMMdd_HHmmss}_{controlId}.hl7";
            var filePath = Path.Combine(DumpFolder, filename);

            await File.WriteAllTextAsync(filePath, strMessage);
            _logger.LogInformation("Message persisted to {Path}", filePath);

            var ackMsg = parsedMessage.GenerateAck(AckTypes.AA, sendingApp, sendingFacility);
            var ackString = pipeParser.Encode(ackMsg);
            string framedAck = $"{(char)0x0B}{ackString}{(char)0x1C}{(char)0x0D}";
            var ackBytes = Encoding.UTF8.GetBytes(framedAck);

            await transport.Output.WriteAsync(ackBytes);
            await transport.Output.FlushAsync();

            await hub.BroadcastAsync($"New message received at {DateTime.Now:HH:mm:ss}");

            _logger.LogInformation("ACK sent for {ControlId} (type {MsgType})", controlId, msgType);
            store.AddMessage(strMessage);
        } 
        catch (Exception ex) 
        {
            _logger.LogError(ex, "HL7 message processing failed. Payload:\n{Message}", strMessage);
        }        
    }
}
