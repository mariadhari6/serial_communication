using System;
using System.Text;
using System.IO.Ports;
using DotNetEnv;
using System.Collections.Generic;

public class Program
{
  private static readonly byte ENQ = 5;
  private static readonly byte ACK = 6;
  private static readonly byte STX = 2;
  private static readonly byte ETX = 3;
  private static readonly byte CR = 13;
  private static readonly byte EOT = 4;
  private static readonly byte ETB = 23;
  private static readonly byte LF = 10;
  private static readonly byte NAK = 21;
  private static readonly int MAX_SEQUENCE = 7;
  private static int nextSequence = 1;

  // Buffer untuk menampung data paket
  private static readonly List<byte> packetBuffer = new List<byte>();
  private static string currentMessage = "";
  private static readonly List<string> messageCollections = new List<string>();

  public enum Mode
  {
    SumMod256, // umum untuk ASTM
    Xor        // beberapa perangkat pakai BCC (XOR)
  }

  private static bool IsValidChecksumAscii(byte[] packet, byte endtx, Mode mode = Mode.SumMod256, bool includeEndTx = true)
  {
    if (packet is null || packet.Length == 0) return false;

    int indexSTX = Array.IndexOf(packet, STX);
    if (indexSTX < 0) return false;

    int indexENDTX = Array.IndexOf(packet, endtx, indexSTX + 1);
    if (indexENDTX < 0) return false;

    // --- Coba format 2 digit ASCII hex ---
    if (indexENDTX + 2 < packet.Length &&
        IsHexAscii(packet[indexENDTX + 1]) &&
        IsHexAscii(packet[indexENDTX + 2]))
    {
      string received = Encoding.ASCII.GetString(packet, indexENDTX + 1, 2);
      string calculated = GenerateChecksumAscii(packet, endtx, mode, includeEndTx);
      // Debug (opsional)
      // Console.WriteLine($"Rx: {received}, Calc: {calculated}");
      return received.Equals(calculated, StringComparison.OrdinalIgnoreCase);
    }

    // --- Fallback: 1 byte biner checksum setelah ENDTX ---
    if (indexENDTX + 1 < packet.Length)
    {
      byte receivedByte = packet[indexENDTX + 1];
      // Hitung byte checksum
      int acc = 0;
      int start = indexSTX + 1;
      int end = includeEndTx ? indexENDTX : indexENDTX - 1;
      if (end < start) return false;

      if (mode == Mode.Xor)
      {
        for (int i = start; i <= end; i++) acc ^= packet[i];
      }
      else
      {
        for (int i = start; i <= end; i++) acc = (acc + packet[i]) & 0xFF;
      }

      return receivedByte == (byte)(acc & 0xFF);
    }

    return false;
  }

  private static bool IsHexAscii(byte b) =>
      (b >= (byte)'0' && b <= (byte)'9') ||
      (b >= (byte)'A' && b <= (byte)'F') ||
      (b >= (byte)'a' && b <= (byte)'f');

  private static string GenerateChecksumAscii(byte[] packet, byte endtx, Mode mode = Mode.SumMod256, bool includeEndTx = true)
  {

    ArgumentNullException.ThrowIfNull(packet);

    int indexSTX = Array.IndexOf(packet, STX);
    if (indexSTX < 0) throw new ArgumentException("STX not found", nameof(packet));

    // Cari ENDTX SETELAH STX
    int indexENDTX = Array.IndexOf(packet, endtx, indexSTX + 1);
    if (indexENDTX < 0) throw new ArgumentException("ENDTX not found", nameof(packet));

    int start = indexSTX + 1;
    int end = includeEndTx ? indexENDTX : indexENDTX - 1;
    if (end < start) throw new ArgumentException("Invalid packet window");

    int acc = mode == Mode.Xor ? 0 : 0;
    for (int i = start; i <= end; i++)
    {
      if (mode == Mode.Xor)
        acc ^= packet[i];
      else
        acc = (acc + packet[i]) & 0xFF; // low 8-bit sum
    }

    if (mode == Mode.SumMod256) acc &= 0xFF;
    return acc.ToString("X2"); // 2 digit hex uppercase
  }

  static void Main(string[] args)
  {
    Env.Load();
    string portName = Environment.GetEnvironmentVariable("SERIAL_PORT") ?? "/dev/pts/2";
    int baudRate = int.Parse(Environment.GetEnvironmentVariable("BAUD_RATE") ?? "9600");
    int dataBits = int.Parse(Environment.GetEnvironmentVariable("DATA_BITS") ?? "8");

    SerialPort serialPort = new SerialPort();
    serialPort.PortName = portName;
    serialPort.BaudRate = baudRate;
    serialPort.Parity = Parity.None;
    serialPort.DataBits = dataBits;
    serialPort.StopBits = StopBits.One;
    serialPort.Handshake = Handshake.None;

    serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);

    try
    {
      serialPort.Open();
      Console.WriteLine("Serial port opened.");
      Console.WriteLine("Press any key to exit...");
      Console.ReadKey();
      serialPort.Close();
    }
    catch (Exception e)
    {
      Console.WriteLine("Error: " + e.Message);
    }
  }

  private static string PacketToText(byte[] packet, bool tailed = false)
  {
    Console.WriteLine("Packet Length: " + packet.Length);
    if (packet[0] != STX)
    {
      throw new ArgumentException("Packet must start with STX");
    }
    if (tailed && (packet.Length < 3 || packet[^1] != ETB))
    {
      throw new ArgumentException("Invalid packet format (tailed)");
    }
    if (!tailed && (packet.Length < 4 || packet[^1] != ETX))
    {
      throw new ArgumentException("Invalid packet format");
    }

    if (!tailed)
    {

      int crIndex = Array.IndexOf(packet, CR, 1);
      if (crIndex == -1 || crIndex != packet.Length - 2)
        throw new ArgumentException("Invalid packet format");

      int textLength = crIndex - 1;
      byte[] textBytes = new byte[textLength];
      Array.Copy(packet, 1, textBytes, 0, textLength);

      return Encoding.UTF32.GetString(textBytes);
    }
    else
    {
      int textLength = packet.Length - 2;
      List<byte> textBytes = [.. packet[1..(1 + textLength)]];
      return Encoding.UTF32.GetString([.. textBytes]);
    }
  }

  private static string ExtractText(byte[] packet, bool tailed = false)
  {
    if (packet[0] != STX)
    {
      throw new ArgumentException("Packet must start with STX");
    }
    if (tailed && (packet.Length < 8 || packet[^5] != ETB))
    {
      throw new ArgumentException("Invalid packet format (tailed)");
    }
    if (!tailed && (packet.Length < 9 || packet[^5] != ETX))
    {
      throw new ArgumentException("Invalid packet format");
    }

    if (!tailed)
    {
      int crIndex = Array.IndexOf(packet, CR, 1);
      if (crIndex == -1 || crIndex != packet.Length - 6)
        throw new ArgumentException("Invalid packet format (tailed)");
      int textLength = crIndex - 1;
      int startIndex = 2;
      List<byte> textBytes = [.. packet[startIndex..(1 + textLength)]];
      return Encoding.UTF32.GetString([.. textBytes]);
    }
    else
    {
      int etbIndex = Array.IndexOf(packet, ETB, 1);
      int textLength = etbIndex - 1;
      int startIndex = 2;
      List<byte> textBytes = [.. packet[startIndex..(1 + textLength)]];
      return Encoding.UTF32.GetString([.. textBytes]);
    }
  }
  private static bool IsValidSequence(byte sequence)
  {
    return (int)sequence == nextSequence;
  }
  private static string GenerateCheckSum(byte[] packet, byte endtx)
  {
    int indexSTX = Array.IndexOf(packet, STX);
    int indexENDTX = Array.IndexOf(packet, endtx);
    if (indexSTX == -1 || indexENDTX == -1 || indexENDTX <= indexSTX)
    {
      throw new ArgumentException("Invalid packet format");
    }
    List<byte> contentBytes = [.. packet[(indexSTX + 1)..indexENDTX]];
    string checkSum = (contentBytes.Count % 256).ToString("X2");
    return checkSum;
  }
  private static bool IsValidChecksum(byte[] packet, byte endtx)
  {
    int indexENDTX = Array.IndexOf(packet, endtx);
    List<byte> checkSumBytes = [.. packet.ToArray().Skip(indexENDTX + 1).Take(2)];
    string receivedCheckSum = Encoding.UTF8.GetString([.. checkSumBytes]);
    string calculatedCheckSum = GenerateCheckSum(packet, endtx);

    Console.WriteLine("Received Checksum: " + receivedCheckSum);
    Console.WriteLine("Calculated Checksum: " + calculatedCheckSum);
    return receivedCheckSum.Equals(calculatedCheckSum, StringComparison.OrdinalIgnoreCase);
  }
  private static void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
  {
    SerialPort sp = (SerialPort)sender;
    int bytesToRead = sp.BytesToRead;
    byte[] buffer = new byte[bytesToRead];
    sp.Read(buffer, 0, bytesToRead);

    foreach (byte b in buffer)
    {
      if (b == ENQ && buffer.Length == 1)
      {
        Console.WriteLine("Received ENQ from Client.");
        sp.Write(new byte[] { ACK }, 0, 1);
        Console.WriteLine("Send ACK to Client.");
        packetBuffer.Clear();
      }
      else if (b == EOT && buffer.Length == 1)
      {
        Console.WriteLine("Received EOT from Client.");
        Console.WriteLine("Transmission ended.");
        string allMessages = string.Join("\n", messageCollections);
        Console.WriteLine("All Messages:\n" + allMessages);
        messageCollections.Clear();
        packetBuffer.Clear();
      }
      else
      {
        packetBuffer.Add(b);

        // Jika sudah menerima CR & LF, proses paket
        if (b == LF)
        {
          try
          {
            // Get ENDTX is ETX or ETB
            byte endTx = packetBuffer[^5];
            if (endTx != ETX && endTx != ETB)
            {
              // throw new ArgumentException("Invalid packet format (missing ETX/ETB)");
              Console.WriteLine("Invalid packet format (missing ETX/ETB)");
              packetBuffer.Clear();
              sp.Write(new byte[] { NAK }, 0, 1);
              Console.WriteLine("Send NAK to Client.");
              return;
            }
            if (!IsValidSequence(packetBuffer[1]))
            {
              Console.WriteLine("Invalid sequence number. Expected: " + nextSequence + ", Received: " + (int)packetBuffer[1]);
              packetBuffer.Clear();
              sp.Write(new byte[] { NAK }, 0, 1);
              Console.WriteLine("Send NAK to Client.");
              return;
            }
            // if (!IsValidChecksum([.. packetBuffer], endTx))
            // {
            //   Console.WriteLine("Invalid checksum.");
            //   packetBuffer.Clear();
            //   // sp.Write(new byte[] { NAK }, 0, 1);
            //   Console.WriteLine("Send NAK to Client.");
            //   return;
            // }
            if (!IsValidChecksumAscii([.. packetBuffer], endTx, Mode.SumMod256, includeEndTx: true))
            {
              Console.WriteLine("Invalid ASCII checksum.");
              if (endTx == ETB)
              {
                Console.WriteLine("THIS IS TAILED PACKET");
              }
              else if (endTx == ETX)
              {
                Console.WriteLine("THIS IS NORMAL PACKET");
              }
              packetBuffer.Clear();
              // sp.Write(new byte[] { NAK }, 0, 1);
              Console.WriteLine("Send NAK to Client.");
              return;
            }
            string text = ExtractText([.. packetBuffer], tailed: endTx == ETB);
            if (endTx == ETB)
            {
              Console.WriteLine("Data Received (Tailed): " + text);
              currentMessage += text;
              packetBuffer.Clear();
              sp.Write(new byte[] { ACK }, 0, 1);
              Console.WriteLine("Send ACK to Client.");

              nextSequence = (nextSequence + 1) % (MAX_SEQUENCE + 1);
            }
            else if (endTx == ETX)
            {
              text = currentMessage + text;
              messageCollections.Add(text);
              Console.WriteLine("Data Received: " + text);
              currentMessage = "";
              packetBuffer.Clear();
              sp.Write(new byte[] { ACK }, 0, 1);
              Console.WriteLine("Send ACK to Client.");

              nextSequence = (nextSequence + 1) % (MAX_SEQUENCE + 1);
            }
          }
          catch (Exception ex)
          {
            Console.WriteLine("Packet error: " + ex.Message);
            packetBuffer.Clear();
            sp.Write(new byte[] { NAK }, 0, 1);
            Console.WriteLine("Send NAK to Client.");
          }
        }
      }
    }
  }
}