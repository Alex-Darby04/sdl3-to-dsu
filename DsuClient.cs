using System.Net;
using System.Net.Sockets;
using System.Text;
using static System.IO.Hashing.Crc32;

class DSUserver
{
    private UdpClient _udpClient = new UdpClient(26760);
    private uint _serverID = (uint)Random.Shared.Next();

    private uint _packetNumber = 0;
    private readonly object _subscriberLock = new();

    private Dictionary<IPEndPoint, DateTime> _subscribers = new();
    private DateTime _lastPruneTime = DateTime.UtcNow;

    private static readonly System.Diagnostics.Stopwatch _timer = System.Diagnostics.Stopwatch.StartNew();
    private readonly byte[] _macAddress = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }; // Safe Unicast MAC
    public async Task CreateClient()
    {
        while (true)
        try
            {
                var result = await _udpClient.ReceiveAsync();
                HandlePacket(result.Buffer, result.RemoteEndPoint);
            }
        catch (SocketException ex) when (ex.NativeErrorCode == 10054)
            {
                // Just ignore "Connection Reset" / Port Unreachable
                continue;
            }
        catch (Exception ex)
            {
                Console.WriteLine($"UDP error: {ex.Message}");
            }
    
    }

    private void HandlePacket(byte[] data, IPEndPoint sender)
    {

        // uint ReadCrc(byte[] data) => BitConverter.ToUInt32(data, 8);
        
        // bool ValidateCrc(byte[] data)
        // {
        //     uint incomingCrc = ReadCrc(data);
            
        //     // CRITICAL: Accept lazy emulator packets if they send a 0 CRC
        //     if (incomingCrc == 0) return true; 

        //     var copy = (byte[])data.Clone();
        //     Array.Clear(copy, 8, 4);
        //     return HashToUInt32(copy) == incomingCrc;
        // }
        
        // if(!ValidateCrc(data)) return;

        
        uint messageType = BitConverter.ToUInt32(data, 16);
        switch(messageType)
        {
            case 0x100000: HandleVersion(sender); break;
            case 0x100001: HandleControllerInfo(sender); break;
            case 0x100002: HandleDataSubscription(sender); 
            HandleDataSubscription(sender);
            break;
            default:
            Console.WriteLine($"[UDP] Unknown Message Type: {messageType}");
            break;
        }
    }

    private byte[] BuildHeader(uint messageType, ushort payloadLength)
    {
        // payloadLength should be the total size of everything AFTER the first 16 bytes
        // (This includes the 4 bytes for the messageType)
        var packet = new byte[16 + payloadLength];
        using var bw = new BinaryWriter(new MemoryStream(packet));

        // --- Official 16-Byte Header ---
        bw.Write(Encoding.ASCII.GetBytes("DSUS")); // 0-3
        bw.Write((ushort)1001);                    // 4-5
        bw.Write(payloadLength);                   // 6-7
        bw.Write((uint)0);                         // 8-11 (CRC Placeholder)
        bw.Write(_serverID);                       // 12-15

        // --- Start of Payload (First 4 bytes) ---
        bw.Write(messageType);                     // 16-19

        return packet;
    }

    private void FillCrc(byte[] packet)
    {
        var crc = HashToUInt32(packet);
        BitConverter.GetBytes(crc).CopyTo(packet, 8);
    }

    private void HandleVersion(IPEndPoint sender)
    {
        // Payload length is 6: 4 bytes (Message Type) + 2 bytes (Version)
        var packet = BuildHeader(0x100000, 6); 
        using var writer = new BinaryWriter(new MemoryStream(packet));
        
        writer.Seek(20, SeekOrigin.Begin); // Safely skip the 16-byte header AND 4-byte Message Type
        
        writer.Write((ushort)1001); // Send the protocol version
        
        FillCrc(packet);
        _udpClient.Send(packet, packet.Length, sender);
    }

    private void HandleControllerInfo(IPEndPoint sender)
    {
        // Payload length is 16: 4 bytes (Message Type) + 12 bytes (Controller Info)
        var packet = BuildHeader(0x100001, 16);
        using var writer = new BinaryWriter(new MemoryStream(packet));
        
        writer.Seek(20, SeekOrigin.Begin); // Safely skip to Offset 20

        writer.Write((byte)0);    // slot 0
        writer.Write((byte)2);    // connected
        writer.Write((byte)2);    // full gyro
        writer.Write((byte)1);    // USB
        writer.Write(_macAddress); // MAC
        writer.Write((byte)0xEF); // charged
        writer.Write((byte)1);    // active

        FillCrc(packet);
        _udpClient.Send(packet, packet.Length, sender);
    }
    private void HandleDataSubscription(IPEndPoint sender)
    {
        lock (_subscriberLock) _subscribers[sender] = DateTime.UtcNow;
    }

    private void PruneStaleSubscribers()
    {
        // Only run this cleanup once every 5 seconds
        if ((DateTime.UtcNow - _lastPruneTime).TotalSeconds < 5) return;
        _lastPruneTime = DateTime.UtcNow;

        var staleThreshold = DateTime.UtcNow.AddSeconds(-10);
        var deadClients = new List<IPEndPoint>();

        // Find who timed out without allocating heavy LINQ closures
        foreach (var sub in _subscribers)
        {
            if (sub.Value < staleThreshold) deadClients.Add(sub.Key);
        }

        // Remove them
        foreach (var client in deadClients)
        {
            _subscribers.Remove(client);
        }
    }

    public void OnMotionUpdated(GamepadTest.MotionData motion)
    {
        // The DSU Motion Payload is exactly 84 bytes. 
        // Header (16) + Payload (84) = 100 bytes total.
        var packet = BuildHeader(0x100002, 84); 
        using var writer = new BinaryWriter(new MemoryStream(packet));

        // Jump to Offset 20! BuildHeader just wrote the Message Type at 16-19.
        writer.Seek(20, SeekOrigin.Begin);

        // --- Offset 20: Controller Info ---
        writer.Write((byte)0);    // 20: slot
        writer.Write((byte)2);    // 21: state (connected)
        writer.Write((byte)2);    // 22: model (full gyro)
        writer.Write((byte)1);    // 23: connection (USB)
        writer.Write(_macAddress); // 24-29: MAC
        writer.Write((byte)0xEF); // 30: battery
        writer.Write((byte)1);    // 31: active state

        // --- Offset 32: Packet Number ---
        writer.Write(_packetNumber++);

        // --- Offset 36: Digital Buttons ---
        writer.Write((byte)0); // 36
        writer.Write((byte)0); // 37
        writer.Write((byte)0); // 38
        writer.Write((byte)0); // 39

        // --- Offset 40: Left & Right Sticks ---
        writer.Write((byte)128); // 40: LX
        writer.Write((byte)128); // 41: LY
        writer.Write((byte)128); // 42: RX
        writer.Write((byte)128); // 43: RY

        // --- Offset 44: Analog Buttons (Pressure sensitive) ---
        writer.Write(new byte[12]); // 44 to 55 (Pad with zeroes)

        // --- Offset 56: Touch 1 ---
        writer.Write((byte)0);   // 56: active
        writer.Write((byte)0);   // 57: id
        writer.Write((ushort)0); // 58: x
        writer.Write((ushort)0); // 60: y

        // --- Offset 62: Touch 2 ---
        writer.Write((byte)0);   // 62: active
        writer.Write((byte)0);   // 63: id
        writer.Write((ushort)0); // 64: x
        writer.Write((ushort)0); // 66: y

        // --- Offset 64: Timestamp ---
        // Calculate microseconds strictly from 0 to prevent Double overflow in Ryujinx
        ulong timestampMicros = (ulong)(_timer.ElapsedTicks * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency);
        writer.Write(timestampMicros); 

        // --- Offset 72: Accelerometer (G-Force) ---
        float gX = (float)(motion.AccelX / 9.8f);
        float gY = (float)(motion.AccelY / 9.8f);
        float gZ = (float)(motion.AccelZ / 9.8f);

        // THE FREEFALL FIX: If SDL3 reports near-zero gravity, force 1G pointing UP.
        if (Math.Abs(gX) < 0.1f && Math.Abs(gY) < 0.1f && Math.Abs(gZ) < 0.1f) 
        {
            gY = 1.0f; 
        }

        writer.Write(gX);
        writer.Write(gY);
        writer.Write(gZ);

        // --- Offset 84: Gyroscope (Deg/Sec) ---
        writer.Write((float)(motion.GyroX * (180f / MathF.PI)));
        writer.Write((float)(motion.GyroY * (180f / MathF.PI)));
        writer.Write((float)(motion.GyroZ * (180f / MathF.PI)));   

        // Finalize CRC
        FillCrc(packet);

        // Broadcast
        lock(_subscriberLock)
        {
            PruneStaleSubscribers();
            foreach (var subscriber in _subscribers.Keys)
            {
                // Use the explicit length overload to guarantee all 100 bytes are sent
                _udpClient.Send(packet, packet.Length, subscriber);
            }
        }
    }
}