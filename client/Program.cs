using System;
using System.IO.Ports;
using System.Text;
using DotNetEnv;
using System.Reflection.Metadata;
public class Program
{

  // private static readonly string TEXT = """
  // Hello Morasaurus
  // Lorem ipsum dolor sit amet, consectetur adipiscing elit. Donec eget laoreet eros.
  // Halo Dunia
  // Hello Dinosaurus
  // こんにちは世界
  // 你好，世界
  // """;
  private static readonly string TEXT = """
  Halo Dunia
  Lorem ipsum dolor sit amet, consectetur adipiscing elit. Nunc scelerisque nisl ac eleifend facilisis. Aliquam sollicitudin eget eros in facilisis. Proin ullamcorper turpis vitae tempus ultrices. Donec justo felis, bibendum at neque vitae, interdum ornare enim. Pellentesque venenatis fringilla commodo. Donec a ligula pellentesque, condimentum tellus pulvinar, vulputate odio. Aenean malesuada sapien turpis, nec hendrerit sem dictum vel.
  Hello Morasaurus
  Hello Dinosaurus
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
  private static int sequence = 1;

  private static readonly int MAX_SEQUENCE = 7;

  private static bool running = true;

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
        Console.WriteLine("Total partition: " + totalPartition);
        Console.WriteLine("Current partition: " + partition);
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
    Console.WriteLine("Partition: " + partition + ", Sequence: " + sequence);

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
      // checkSum = GenerateCheckSum([.. packet], ETB);
      checkSum = GenerateChecksumAscii(packet.ToArray(), ETB, Mode.SumMod256, true);
    }
    else
    {
      // [STX][TEXT][CR][ETX]
      packet = [STX, (byte)currentSequence, .. content, CR, ETX];
      Console.WriteLine("Generate Checksum FOR ETX");
      checkSum = GenerateChecksumAscii(packet.ToArray(), ETX, Mode.SumMod256, true);
      Console.WriteLine("Checksum: " + checkSum);
      // checkSum = GenerateCheckSum([.. packet], ETX);
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
}