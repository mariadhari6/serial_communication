using System;
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

  // Buffer untuk menampung data paket
  private static List<byte> packetBuffer = new List<byte>();

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

  private static string PacketToText(byte[] packet)
  {
    Console.WriteLine("Packet Length: " + packet.Length);
    if (packet.Length < 4 || packet[0] != STX || packet[^1] != ETX)
      throw new ArgumentException("Invalid packet format");

    int crIndex = Array.IndexOf(packet, CR, 1);
    if (crIndex == -1 || crIndex != packet.Length - 2)
      throw new ArgumentException("Invalid packet format");

    int textLength = crIndex - 1;
    byte[] textBytes = new byte[textLength];
    Array.Copy(packet, 1, textBytes, 0, textLength);

    return System.Text.Encoding.ASCII.GetString(textBytes);
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
      }
    }
  }
}