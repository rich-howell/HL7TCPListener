using Microsoft.AspNetCore.Connections;
using NHapi.Base.Parser;
using NHapiTools.Base;
using NHapiTools.Base.Util;
using System.Buffers;
using System.Text;

namespace HL7Listener;

/// <summary>
/// Handles incoming TCP connections for HL7 message processing using the Minimal Lower Layer Protocol (MLLP).
/// This class is responsible for reading MLLP frames, parsing HL7 messages, and sending acknowledgments.
/// </summary>
public class HL7ConnectionHandler
{
    private readonly ILogger _logger;
    private readonly PipeParser pipeParser;

    /// <summary>
    /// Initializes a new instance of the HL7ConnectionHandler class.
    /// </summary>
    /// <param name="pipeParser">The NHapi PipeParser instance used for parsing HL7 messages.</param>
    /// <param name="loggerFactory">The logger factory for creating logger instances.</param>
    public HL7ConnectionHandler(PipeParser pipeParser,ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<HL7ConnectionHandler>();
        this.pipeParser = pipeParser;
    }

    /// <summary>
    /// Handles the main connection processing loop for an incoming TCP connection.
    /// Continuously reads MLLP frames from the connection and processes HL7 messages.
    /// </summary>
    /// <param name="connectionContext">The connection context representing the TCP connection.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
    /// Attempts to read a complete MLLP frame from the input buffer.
    /// MLLP frames are delimited by start-of-block (0x0B) and end-of-block (0x1C) characters.
    /// </summary>
    /// <param name="buffer">The input buffer containing potentially multiple MLLP frames.</param>
    /// <param name="frame">When this method returns true, contains the extracted MLLP frame.</param>
    /// <returns>True if a complete MLLP frame was found and extracted; otherwise, false.</returns>
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
    /// Processes a received HL7 message by parsing it and generating an appropriate acknowledgment (ACK).
    /// The method extracts the HL7 content from the MLLP frame, parses it using NHapi,
    /// and sends back an ACK message wrapped in MLLP format.
    /// </summary>
    /// <param name="message">The MLLP frame containing the HL7 message.</param>
    /// <param name="transport">The duplex pipe for sending the acknowledgment response.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
