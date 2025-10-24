using Microsoft.AspNetCore.Connections;
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

    public HL7ConnectionHandler(
        PipeParser pipeParser,
        ILoggerFactory loggerFactory,
        IConfiguration config,
        MessageStore store)
    {
        _logger = loggerFactory.CreateLogger<HL7ConnectionHandler>();
        this.pipeParser = pipeParser;
        this.loggerFactory = loggerFactory;
        this.config = config;
        this.store = store;

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
        Directory.CreateDirectory(DumpFolder);

        _logger.LogInformation("Connected to {remotenode}", connectionContext.RemoteEndPoint);
        while (true)
        {
            // Read the next frame
            var result = await connectionContext.Transport.Input.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;

            while (TryReadMllpFrame(ref buffer, out ReadOnlySequence<byte> frame))
            {
                // Process the MLLP frame
                await ProcessHl7Message(frame,connectionContext.Transport);
            }

            // Tell the PipeReader how much of the buffer has been consumed.
            connectionContext.Transport.Input.AdvanceTo(buffer.Start, buffer.End);

            // Stop reading   
            //if there's no more data coming.
            if (result.IsCompleted)
            {
                break;
            }
        }

        await connectionContext.Transport.Input.CompleteAsync();
        _logger.LogInformation("Disconneded from  {remotenode}", connectionContext.RemoteEndPoint);
    }

    private bool TryReadMllpFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> frame)
    {
        var start = buffer.PositionOf((byte)0x0B);
        var end = buffer.PositionOf((byte)0x1C);
        if (start != null && end != null)
        {
            frame = buffer.Slice(start.Value , buffer.GetPosition(1, end.Value));
            buffer = buffer.Slice(buffer.GetPosition(1, end.Value));
            return true;
        }
        frame = default;
        return false;
    }

    private async Task ProcessHl7Message(ReadOnlySequence<byte> message,System.IO.Pipelines.IDuplexPipe transport)
    {
        try
        {
            var mllpFrame = message.Slice(1, message.Length - 2);
            var strMessage = Encoding.UTF8.GetString(mllpFrame);

            _logger.LogInformation("Received HL7 Message {msg}", strMessage);

            var parsedMessage = pipeParser.Parse(strMessage.TrimEnd('\n'));
            var terser = new NHapi.Base.Util.Terser(parsedMessage);

            var controlId = terser.Get("/MSH-10") ?? Guid.NewGuid().ToString();

            string filename = $"{DateTime.Now:yyyyMMdd_HHmmss}_{controlId}.hl7";
            string filePath = Path.Combine(DumpFolder, filename);
            await File.WriteAllTextAsync(filePath, strMessage);
            _logger.LogInformation("Message written to {path}", filePath);

            var sendingApp = terser.Get("/MSH-3") ?? "UnknownApp";
            var sendingFacility = terser.Get("MSH-4") ?? "UnknownFacility";

            var ackMsg = parsedMessage.GenerateAck(AckTypes.AA, sendingApp, sendingFacility);
            var ackString = pipeParser.Encode(ackMsg);

            string framedAck = $"{(char)0x0B}{ackString}{(char)0x1C}{(char)0x0D}";
            var ackBytes = Encoding.UTF8.GetBytes(framedAck);

            var parts = strMessage.Split('\r');
            var hl7Msg = string.Join("\r\n", strMessage.Split('\r'));

            await transport.Output.WriteAsync(ackBytes);
            await transport.Output.FlushAsync();

            _logger.LogInformation("ACK sent for control ID {id}", controlId);

            store.AddMessage(strMessage);
        } 
        catch (Exception ex) 
        {
            _logger.LogError(ex, "HL7 message processing failed");
        }        
    }
}
