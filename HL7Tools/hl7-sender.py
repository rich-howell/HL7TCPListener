import socket
import time

# --- CONFIG ---
HOST = "127.0.0.1"   # localhost
PORT = 4040          # your listener port

# --- Example HL7 Message ---
# You can replace this with anything you like (e.g. an ADT, ORU, ORM message)
hl7_message = (
    "MSH|^~\\&|SendingApp|SendingFac|ReceivingApp|ReceivingFac|"
    f"{time.strftime('%Y%m%d%H%M%S')}||ADT^A01|MSG00001|P|2.5\r"
    "PID|1||123456^^^Hospital^MR||Doe^John||19800101|M|||123 Street^^Town^CT^12345||555-5555|\r"
)

# --- Add MLLP framing ---
SB = b"\x0b"   # Start Block <VT>
EB = b"\x1c"   # End Block <FS>
CR = b"\x0d"   # Carriage Return <CR>

framed_message = SB + hl7_message.encode("utf-8") + EB + CR

print(f"Connecting to {HOST}:{PORT}...")
with socket.create_connection((HOST, PORT)) as sock:
    print("Connected! Sending HL7 message...")
    sock.sendall(framed_message)

    # Wait for ACK
    ack_data = b""
    sock.settimeout(5)  # wait max 5 seconds for a response
    try:
        while True:
            chunk = sock.recv(4096)
            if not chunk:
                break
            ack_data += chunk
            if b"\x1c\x0d" in ack_data:  # ACK frame complete
                break
    except socket.timeout:
        print("Timed out waiting for ACK")

# Decode and strip MLLP framing
if ack_data:
    ack_str = ack_data.strip(b"\x0b\x1c\x0d").decode("utf-8", errors="ignore")
    print("\nReceived ACK:\n-----------------\n")
    print(ack_str)
else:
    print("No ACK received.")
