using Microsoft.AspNetCore.Connections;
using NHapi.Base.Parser;
using NHapiTools.Base;
using NHapiTools.Base.Util;
using System.Buffers;
using System.Text;

namespace HL7Listener;

public class HL7ConnectionHandler
{
    private readonly ILogger _logger;
    private readonly PipeParser pipeParser;

    public HL7ConnectionHandler(PipeParser pipeParser,ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<HL7ConnectionHandler>();
        this.pipeParser = pipeParser;
    }

    public async Task HandleConnection(ConnectionContext connectionContext)
    {
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

    /// <summary>
    /// Process Minimal Lower Layer Protocol (MLLP) frame
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="frame"></param>
    /// <returns></returns>
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
    /// <summary>
    /// Process HL7 Message
    /// </summary>
    /// <param name="message"></param>
    private async Task ProcessHl7Message(ReadOnlySequence<byte> message, 
        System.IO.Pipelines.IDuplexPipe transport)
    {
        var mllpFrame = message.Slice(1, message.Length - 2);
        // Implement your HL7 message processing logic here
        // For example, parse the message, validate it, and send a response
        var strMessage = Encoding.UTF8.GetString(mllpFrame);//.TrimEnd('\r').TrimEnd('\n');
        var parts = strMessage.Split('\r');
        var hl7Msg = string.Join("\r\n",strMessage.Split('\r'));
        _logger.LogInformation("Received HL7 Message {msg}", hl7Msg);
        try
        {
            var parsedMessage = pipeParser.Parse(strMessage.TrimEnd('\n'));
            var ackMsg = parsedMessage.GenerateAck(AckTypes.AA, "demoapp", "test");
            var ackString =  pipeParser.Encode(ackMsg);

            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append((byte)0x0B);
            stringBuilder.Append(ackString);
            stringBuilder.Append((byte)0x1C);
            stringBuilder.Append('\n');
            var ackBytes = Encoding.UTF8.GetBytes(stringBuilder.ToString());
            await transport.Output.WriteAsync(ackBytes);
        }
        catch (Exception exp)
        {

            _logger.LogError(exp, "HL7 message parsing failed");
        }
        
    }
}
