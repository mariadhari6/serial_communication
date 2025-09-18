using System;
using System.Text;
using System.IO.Ports;
using DotNetEnv;
using System.Collections.Generic;

public class Program
{
  private static byte ENQ = 5;
  private static byte ACK = 6;
  private static byte STX = 2;
  private static byte ETX = 3;
  private static byte CR = 13;
  private static byte EOT = 4;
  private static byte ETB = 23;

  // Buffer untuk menampung data paket
  private static List<byte> packetBuffer = new List<byte>();
  private static List<string> messageCollections = new List<string>();

  static void Main(string[] args)
  {
    DotNetEnv.Env.Load();
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
    if (tailed && packet.Length < 3 || packet[^1] != ETB)
    {
      throw new ArgumentException("Invalid packet format");
    }
    if (!tailed && packet.Length < 4 || packet[^1] != ETX)
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

  private static void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
  {
    SerialPort sp = (SerialPort)sender;
    int bytesToRead = sp.BytesToRead;
    byte[] buffer = new byte[bytesToRead];
    sp.Read(buffer, 0, bytesToRead);

    foreach (byte b in buffer)
    {
      if (b == ENQ)
      {
        Console.WriteLine("Received ENQ from Client.");
        sp.Write(new byte[] { ACK }, 0, 1);
        Console.WriteLine("Send ACK to Client.");
        packetBuffer.Clear();
      }
      else if (b == EOT)
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
        if (b == ETX && packetBuffer.Count >= 4 && packetBuffer[0] == STX)
        {
          try
          {
            string text = PacketToText(packetBuffer.ToArray());
            messageCollections.Add(text);
            Console.WriteLine("Data Received: " + text);
            sp.Write(new byte[] { ACK }, 0, 1);
            Console.WriteLine("Send ACK to Client.");
          }
          catch (Exception ex)
          {
            Console.WriteLine("Packet error: " + ex.Message);
          }
          packetBuffer.Clear();
        }

        else if (b == ETB && packetBuffer.Count >= 3 && packetBuffer[0] == STX)
        {
          try
          {
            string text = PacketToText(packetBuffer.ToArray(), tailed: true);
            // messageCollections.Add(text);
            messageCollections[^1] += text; // Append to last message
            Console.WriteLine("Data Received (Tailed): " + text);
            sp.Write(new byte[] { ACK }, 0, 1);
            Console.WriteLine("Send ACK to Client.");
          }
          catch (Exception ex)
          {
            Console.WriteLine("Packet error: " + ex.Message);
          }
          packetBuffer.Clear();
        }
      }
    }
  }
}