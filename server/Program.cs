using System;
using System.IO.Ports;

public class Program
{
  static void Main(string[] args)
  {
    SerialPort serialPort = new SerialPort();
    serialPort.PortName = "/dev/pts/2";
    serialPort.BaudRate = 9600;
    serialPort.Parity = Parity.None;
    serialPort.DataBits = 8;
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
  private static void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
  {
    SerialPort sp = (SerialPort)sender;
    string data = sp.ReadExisting();
    Console.WriteLine("Data Received: " + data);
  }
}