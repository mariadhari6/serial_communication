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

        // Jika sudah menerima ETX, proses paket
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
            string text = ExtractText(packetBuffer.ToArray(), tailed: endTx == ETB);
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