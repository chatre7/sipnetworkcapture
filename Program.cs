using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using PacketDotNet;
using SharpPcap;
using System.Text;

namespace SIPNetworkCapture
{
    internal class Program
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static readonly IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
        static void Main(string[] args)
        {
            try
            {


                string DeviceName = configuration["AppSettings:DeviceName"];
                logger.Info("Hello, World!");
                logger.Info($"DeviceName: {DeviceName}");
                var devices = CaptureDeviceList.Instance;

                if (devices.Count < 1)
                {
                    logger.Info("No devices were found on this machine");
                    return;
                }

                int i = 1;
                foreach (var device in devices)
                {
                    logger.Info($"{i}) {device.Description}");
                    i++;
                }

                var network = devices.FirstOrDefault(x => x.Description == DeviceName);
                if (network != null)
                {
                    network.OnPacketArrival += Network_OnPacketArrival;
                    network.OnCaptureStopped += Network_OnCaptureStopped;
                    int readTimeoutMilliseconds = 1000;
                    network.Open(DeviceModes.Promiscuous, readTimeoutMilliseconds);
                    string filter = "udp port 5060";
                    network.Filter = filter;
                    logger.Info("Capture Started...");
                    network.Capture();
                    network.Close();
                }
                else
                {
                    logger.Info("No devices were found for capture");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                throw;
            }
        }


        private static void Network_OnPacketArrival(object sender, PacketCapture e)
        {
            try
            {
                var package = e.GetPacket();
                var packet = Packet.ParsePacket(package.LinkLayerType, package.Data);
                var udpPacket = packet.Extract<UdpPacket>();
                if (udpPacket != null)
                {
                    try
                    {
                        var payloadData = Encoding.ASCII.GetString(udpPacket.PayloadData);
                        var ipPacket = (IPPacket)udpPacket.ParentPacket;
                        if (udpPacket.DestinationPort == 5060 || udpPacket.SourcePort == 5060)
                        {
                            //Console.WriteLine(payloadData);
                            //Console.WriteLine("***********************************************");
                            ParseSdpMessage(payloadData);
                            string json = ConvertSipToJson(payloadData);
                            if (!String.IsNullOrEmpty(json))
                            {
                                logger.Info($"json={json}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
        }

        private static void Network_OnCaptureStopped(object sender, CaptureStoppedEventStatus status)
        {
            throw new NotImplementedException();
        }

        private static void ParseSdpMessage(string sdpMessage)
        {
            if (!String.IsNullOrEmpty(sdpMessage.Trim().Trim()))
            {

                var lines = sdpMessage.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    if (line.StartsWith("v="))
                    {
                        Console.WriteLine("SDP Version: " + line.Substring(2));
                    }
                    else if (line.StartsWith("o="))
                    {
                        Console.WriteLine("Origin: " + line.Substring(2));
                    }
                    else if (line.StartsWith("s="))
                    {
                        Console.WriteLine("Session Name: " + line.Substring(2));
                    }
                    else if (line.StartsWith("c="))
                    {
                        Console.WriteLine("Connection Information: " + line.Substring(2));
                    }
                    else if (line.StartsWith("m="))
                    {
                        Console.WriteLine("Media Description: " + line.Substring(2));
                    }
                    // Additional parsing for other SDP fields as needed
                }
            }
        }

        public static string ConvertSipToJson(string payloadData)
        {
            if (!String.IsNullOrEmpty(payloadData.Trim().Trim()))
            {
                var lines = payloadData.Split(new[] { "\r\n" }, StringSplitOptions.None);
                var jsonDict = new Dictionary<string, object>();

                // Parse Request Line
                var requestLineParts = lines[0].Split(' ');
                if (requestLineParts.Length == 3)
                {
                    var requestLine = new Dictionary<string, string>
            {
                { "method", requestLineParts[0].Trim() },
                { "uri", requestLineParts[1].Trim() },
                { "version", requestLineParts[2].Trim() }
            };
                    jsonDict["request"] = requestLine;
                }

                // Parse Headers
                var headersDict = new Dictionary<string, string>();
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;

                    var headerParts = lines[i].Split(new[] { ": " }, 2, StringSplitOptions.None);
                    if (headerParts.Length == 2)
                    {
                        headersDict[headerParts[0]] = headerParts[1].Trim();
                    }
                }
                jsonDict["headers"] = headersDict;
                return JsonConvert.SerializeObject(jsonDict, Newtonsoft.Json.Formatting.Indented);

            }
            else
            {
                return null;
            }
        }
    }
}
