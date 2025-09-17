using System;
using System.IO.Ports;

public class Program
{

  private static string TEXT = """
        Hello World
        Halo Dunia
        Hello Morasaurus
        Hello Dinosaurs
        こんにちは世界
        你好，世界
  """;
  private static byte ENQ = 5;
  private static byte ACK = 6;

  private static int index = 0;

  private static bool running = true;
  public static void Main(string[] args)
  {
    SerialPort serialPort = new SerialPort();
    serialPort.PortName = "/dev/pts/1";
    serialPort.BaudRate = 9600;
    serialPort.Parity = Parity.None;
    serialPort.DataBits = 8;
    serialPort.StopBits = StopBits.One;
    serialPort.Handshake = Handshake.None;

    serialPort.DataReceived += new SerialDataReceivedEventHandler(ACKReceiveHandler);

    try
    {
      serialPort.Open();
      // Send ENQ to Server
      Console.WriteLine("Serial port opened.");
      serialPort.Write(new byte[] { ENQ }, 0, 1);
      Console.WriteLine("Send ENQ to Server.");

      while (running)
      {
        System.Threading.Thread.Sleep(100); // Wait for the program to finish sending all text
      }
      serialPort.Close();
    }
    catch (Exception e)
    {
      Console.WriteLine("Error: " + e.Message);
    }
  }
  private static void ACKReceiveHandler(object sender, SerialDataReceivedEventArgs e)
  {
    SerialPort sp = (SerialPort)sender;
    int data = sp.ReadByte();
    if (data == ACK)
    {
      Console.WriteLine("Received ACK from Server.");
      SendText(sp);
    }
  }
  private static string? GetText(int line)
  {
    string[] lines = TEXT.Split('\n');
    if (line < lines.Length)
    {
      return lines[line];
    }
    else
    {
      return null;
    }
  }
  private static void SendText(SerialPort sp)
  {
    string? text = GetText(index);
    index++;
    if (text == null)
    {
      Console.WriteLine("All text sent.");
      byte EOT = 4;
      // Send EOT to Server
      sp.Write(new byte[] { EOT }, 0, 1);
      Console.WriteLine("Send EOT to Server.");
      sp.Close();
      // Stop the program
      running = false;
      return;
    }
    //  [STX][TEXT][CR][ETX]
    byte STX = 2;
    byte ETX = 3;
    byte CR = 13;
    byte[] textBytes = System.Text.Encoding.ASCII.GetBytes(text);
    byte[] packet = new byte[3 + textBytes.Length];
    packet[0] = STX;
    Array.Copy(textBytes, 0, packet, 1, textBytes.Length);
    packet[1 + textBytes.Length] = CR;
    packet[2 + textBytes.Length] = ETX;
    sp.Write(packet, 0, packet.Length);
    Console.WriteLine("Sent text to Server: " + text.Trim());
  }
}