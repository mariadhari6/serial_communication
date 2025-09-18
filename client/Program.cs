using System;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Text;
using DotNetEnv;
using System.Linq;
public class Program
{

  private static string TEXT = """
        Lorem ipsum dolor sit amet, consectetur adipiscing elit. Donec eget laoreet eros. In ante turpis, venenatis in condimentum id, scelerisque a nisi. Praesent ullamcorper scelerisque lectus, nec mollis ipsum finibus sed. Pellentesque congue felis nibh, sit amet hendrerit mi consequat at. Proin tempor quis turpis non semper. Ut vel diam aliquam, rhoncus enim eget, accumsan est. Nunc tempus tincidunt sapien ac tincidunt. Morbi ac iaculis odio. Aenean tristique sagittis risus, euismod vehicula nulla sagittis facilisis. Quisque orci massa, sollicitudin sit amet eros a, finibus tempus mi. Aenean maximus dapibus pharetra. Nam tempor auctor massa, nec iaculis diam facilisis ac. Sed posuere, nunc eget gravida suscipit, velit mi blandit quam, a elementum odio velit ac justo. Aenean orci felis, aliquet non dolor eu, vestibulum posuere nisl. Etiam at aliquet enim, ut pulvinar est. Maecenas in malesuada elit 
        Halo Dunia
        Hello Morasaurus
        Hello Dinosaurus
        こんにちは世界
        你好，世界
  """;
  private static byte ENQ = 5;
  private static byte ACK = 6;
  private static byte ETB = 23;
  private static byte EOT = 4;
  private static int line = 0;
  private static byte STX = 2;
  private static byte ETX = 3;
  private static byte CR = 13;
  private static int partition = 0;

  private static int maxCharactersPerLine = 50;

  private static bool running = true;
  public static void Main(string[] args)
  {

    // Load env variable
    DotNetEnv.Env.Load();
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
  private static string? GetText()
  {
    string[] lines = TEXT.Split('\n');
    if (line < lines.Length)
    {
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

    List<byte> packet = new List<byte>();
    if (partition > 0)
    {
      // [STX][TEXT][ETB]
      packet = [STX, .. Encoding.UTF32.GetBytes(text), ETB];
    }
    else
    {
      // [STX][TEXT][CR][ETX]
      packet = [STX, .. Encoding.UTF32.GetBytes(text), CR, ETX];
    }
    sp.Write(packet.ToArray(), 0, packet.Count);
    Console.WriteLine("Sent text to Server: " + text.Trim());
  }
}