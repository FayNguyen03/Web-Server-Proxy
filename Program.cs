using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;


namespace WebProxy{
    public static class Globals
    {
        public const Int32 PORT_NUMBER = 3000;
        public static readonly HttpClient httpClient = new HttpClient();

        public static Encoding ascii = Encoding.ASCII;
    }
    internal static class Program
    {
        static void Main(){
            StartProxy();
        }

        static void StartProxy()
        {
            
            //accept client connections
            TcpListener listener = new TcpListener(IPAddress.Any, Globals.PORT_NUMBER);
            listener.Start();
            Console.WriteLine($"Proxy Server started on port {Globals.PORT_NUMBER}");

            while(true){
                TcpClient client = listener.AcceptTcpClient();
                Task.Run(() => HandleClient(client));
            }
        }

        static async Task HandleClient(TcpClient client){
            using (NetworkStream clientStream = client.GetStream()){
                StreamReader reader = new StreamReader(clientStream, Globals.ascii);
                StreamWriter writer = new StreamWriter(clientStream, new UTF8Encoding(false)) {AutoFlush = true};

                //Read the HTTP request
                string requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(requestLine)){
                    client.Close();
                    return;
                }
                Console.WriteLine($"\nReceived Request: {requestLine}");

                //Parse method and URL
                string[] requestParts = requestLine.Split(' ');
                if (requestParts.Length < 2){
                    Console.WriteLine("Invalid Request");
                    client.Close();
                    return;
                }

                string method = requestParts[0];
                string url = requestParts[1];

                //Reading the headers
                string headers = "";
                string line;

                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())){
                    headers += line + "\n";
                }

                Console.WriteLine($"Method: {method}");
                Console.WriteLine($"URL: {url}");
                Console.WriteLine("Headers:\n" + headers);

                client.Close();
            }
        }
    
    }
}
