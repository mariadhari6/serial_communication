using System;
using System.IO.Ports;
using System.Text;
using DotNetEnv;
using System.Reflection.Metadata;
public class Program
{

  private static readonly string TEXT = """
  Lorem ipsum dolor sit amet, consectetur adipiscing elit. Donec eget laoreet eros. 
  Halo Dunia
  Hello Morasaurus
  Hello Dinosaurus
  こんにちは世界
  你好，世界
  """;
  private static readonly byte ENQ = 5;
  private static readonly byte ACK = 6;
  private static readonly byte ETB = 23;
  private static readonly byte EOT = 4;
  private static int line = 0;
  private static readonly byte STX = 2;
  private static readonly byte ETX = 3;
  private static readonly byte CR = 13;
  private static readonly byte LF = 10;
  private static readonly byte NAK = 21;
  private static int partition = 0;
  private static int lastPartition = partition;

  private static readonly int maxCharactersPerLine = 50;
  private static int sequence = 0;

  private static readonly int MAX_SEQUENCE = 7;

  private static bool running = true;
  public static void Main(string[] args)
  {

    // Load env variable
    Env.Load();
    string portName = Environment.GetEnvironmentVariable("SERIAL_PORT") ?? "/dev/pts/1";
    int baudRate = int.Parse(Environment.GetEnvironmentVariable("BAUD_RATE") ?? "9600");
    int dataBits = int.Parse(Environment.GetEnvironmentVariable("DATA_BITS") ?? "8");


    SerialPort serialPort = new SerialPort();
    serialPort.PortName = portName;
    serialPort.BaudRate = baudRate;
    serialPort.Parity = Parity.None;
    serialPort.DataBits = dataBits;
    serialPort.StopBits = StopBits.One;
    serialPort.Handshake = Handshake.None;


    serialPort.DataReceived += new SerialDataReceivedEventHandler(ReceiveHandler);

    try
    {
      serialPort.Open();
      // Send ENQ to Server
      Console.WriteLine("Serial port opened.");
      serialPort.Write(new byte[] { ENQ }, 0, 1);
      Console.WriteLine("Send ENQ to Server.");

      while (running)
      {
        Thread.Sleep(100); // Wait for the program to finish sending all text
      }
      serialPort.Close();
    }
    catch (Exception e)
    {
      Console.WriteLine("Error: " + e.Message);
    }
  }
  private static void ReceiveHandler(object sender, SerialDataReceivedEventArgs e)
  {
    SerialPort sp = (SerialPort)sender;
    int data = sp.ReadByte();
    if (data == ACK)
    {
      Console.WriteLine("Received ACK from Server.");
      SendText(sp);

    }
    else if (data == NAK)
    {
      Console.WriteLine("Received NAK from Server.");
      // Resend the last text
      // partition = Math.Max(0, partition - 1);
      sequence = sequence == 0 ? MAX_SEQUENCE : sequence - 1;
      if (partition == 0 && line > 0)
      {
        line = Math.Max(0, line - 1);
      }
      partition = lastPartition;
      SendText(sp);
    }
    else
    {
      Console.WriteLine("Received unknown data from Server: " + data);
    }
  }
  private static string? GetText()
  {
    string[] lines = TEXT.Split('\n');
    if (line < lines.Length)
    {
      lastPartition = partition;
      string text;
      if (lines[line].Length <= maxCharactersPerLine)
      {
        text = lines[line];
        line++;
        partition = 0;
      }
      else
      {
        int totalPartition = (int)Math.Ceiling((double)lines[line].Length / maxCharactersPerLine);
        int startIndex = partition * maxCharactersPerLine;
        // split string each maxCharactersPerLine characters
        text = lines[line].Substring(startIndex, Math.Min(maxCharactersPerLine, lines[line].Length - startIndex));
        partition++;
        if (partition >= totalPartition)
        {
          line++;
          partition = 0;
        }
      }
      return text;
    }
    return null;
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
  private static void SendText(SerialPort sp)
  {
    string? text = GetText();

    if (text == null)
    {
      Console.WriteLine("All text sent.");
      // Send EOT to Server
      sp.Write(new byte[] { EOT }, 0, 1);
      Console.WriteLine("Send EOT to Server.");
      sp.Close();
      // Stop the program
      running = false;
      return;
    }

    List<byte> packet;
    byte[] content = Encoding.UTF32.GetBytes(text);
    string checkSum;
    bool simulationMode = Environment.GetEnvironmentVariable("SIMULATION_MODE") == "true";
    int currentSequence = sequence;
    if (simulationMode && new Random().Next(0, 2) == 0)
    {
      // simulate error by changing sequence number to 9
      // random booleancurrentSequence = 9;
      currentSequence = 9;
      Console.WriteLine("Simulation mode: sending wrong sequence number");
    }
    if (partition > 0)
    {
      // [STX][TEXT][ETB]
      packet = [STX, (byte)currentSequence, .. content, ETB];
      checkSum = GenerateCheckSum([.. packet], ETB);
    }
    else
    {
      // [STX][TEXT][CR][ETX]
      packet = [STX, (byte)currentSequence, .. content, CR, ETX];
      checkSum = GenerateCheckSum([.. packet], ETX);
    }
    if (simulationMode && new Random().Next(0, 2) == 0)
    {
      // simulate error by changing checksum to "FF"
      checkSum = "FF";
      Console.WriteLine("Simulation mode: sending wrong checksum");
    }

    byte[] checkSumBytes = Encoding.UTF8.GetBytes(checkSum);
    packet.AddRange([.. checkSumBytes, CR, LF]);

    sp.Write(packet.ToArray(), 0, packet.Count);
    Console.WriteLine("Sent text to Server: " + text.Trim());
    if (sequence < MAX_SEQUENCE)
    {
      sequence++;
    }
    else
    {
      sequence = 0;
    }
  }

  private static object Random()
  {
    throw new NotImplementedException();
  }
}