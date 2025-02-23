using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;
using System.Net.Http.Headers;


namespace WebProxy{
    public static class Globals
    {
        public const Int32 PORT_NUMBER = 4000;
        public static readonly HttpClient httpClient = new HttpClient();

        public static Encoding ascii = Encoding.ASCII;

        //blocked URL set
        public static HashSet<string> blockedURLS = new HashSet<string>();

        public static Dictionary<string, (DateTime, byte[])> cache = new Dictionary<string, (DateTime, byte[])>();

        public static DateTime start;

        public static DateTime end;
    }
    internal static class Program
    {
        static void Main(){
            
            //Proxy in a seperate thread
            Thread proxyThread = new Thread(StartProxy);
            proxyThread.Start();
            
            while(true){
                Console.WriteLine("Enter URL block commands (block - B, unblock - U, list - L): ");
                string command = Console.ReadLine();
                if (!string.IsNullOrEmpty(command)){
                    ConsoleCommand(command);
                }
            }
            
        }

        static void ClosingClient(TcpClient client){
            Console.WriteLine($"\nClose the Client");
            client.Close();
        }
        static void StartProxy()
        {
            
            //accept client connections
            TcpListener listener = new TcpListener(IPAddress.Any, Globals.PORT_NUMBER);
            listener.Start();
            Console.WriteLine($"Proxy Server started on port {Globals.PORT_NUMBER}");

            while(true){
                TcpClient client = listener.AcceptTcpClient();
                //Thread thread = new Thread(() => HandleClient(client));
                //thread.Start();
                ThreadPool.QueueUserWorkItem(state => HandleClient(client));
                //Using threadpool can be more efficient since this reuses threads instead of creating a new thread/request
            }
        }

        static async Task HandleClient(TcpClient client){
            using (NetworkStream clientStream = client.GetStream()){
                StreamReader reader = new StreamReader(clientStream, Globals.ascii);
                StreamWriter writer = new StreamWriter(clientStream, new UTF8Encoding(false)) {AutoFlush = true};

                //Read the HTTP request
                string requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(requestLine)){
                    ClosingClient(client);
                    return;
                }
                Console.WriteLine($"\nReceived Request: {requestLine}");

                //Parse method and URL
                string[] requestParts = requestLine.Split(' ');
                if (requestParts.Length < 2){
                    Console.WriteLine("Invalid Request");
                    ClosingClient(client);
                    return;
                }

                string method = requestParts[0];
                string url = Normalize(requestParts[1]);

                if (Globals.blockedURLS.Contains(url)){
                    Console.WriteLine($"Urgh Oh! {url} is blocked!");
                    string response = "HTTP/1.1 403 Forbidden";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    
                    await clientStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    ClosingClient(client);
                    return;
                }

                Console.WriteLine($"Method: {method}");
                Console.WriteLine($"URL: {url}");
            
                //Check Cache before Fetching
                if (method == "GET" && Globals.cache.ContainsKey(url)){
                    Globals.start = DateTime.Now;
                    var (timestamp, cachedData) = Globals.cache[url];

                    //Expire cache after 1 minute
                    if((DateTime.Now - timestamp).TotalMinutes < 1){
                        
                        Console.WriteLine($"[CACHE HIT] {url}");
                        clientStream.Write(cachedData, 0, cachedData.Length);
                        Globals.end = DateTime.Now;
                        Console.WriteLine($"[CACHE TIMING] {url} took {(Globals.end - Globals.start).TotalMilliseconds} ms");
                        ClosingClient(client);
                        return;
                    }
                    else{
                        Globals.cache.Remove(url);
                    }

                }

                //Handle HTTPS CONNECT requests
                if (method.ToUpper() == "CONNECT"){
                    string[] hostParts = url.Split(":");
                    string host = hostParts[0];
                    int port = hostParts.Length > 1 ? int.Parse(hostParts[1]) : 3000;

                    Console.WriteLine($"Establishing tunnel to {host}:{port}");

                    using (TcpClient server = new TcpClient(host, port))
                    using (NetworkStream serverStream = server.GetStream())
                    {
                        // Send "200 Connection Established" response to the client
                        await writer.WriteLineAsync("HTTP/1.1 200 Connection Established");
                        await writer.WriteLineAsync("Proxy-Agent: C# Proxy");
                        await writer.WriteLineAsync(); // End headers

                        // Relay encrypted traffic; Ensure both directions (client -> server and server -> client_ run) until the connection closes)
                        await Task.WhenAll(
                            clientStream.CopyToAsync(serverStream),
                            serverStream.CopyToAsync(clientStream)
                        );

                    }
                    ClosingClient(client);
                    return;
                }

                //Forward request to the actual web server
                try{
                    HttpRequestMessage forwardRes = new HttpRequestMessage(new HttpMethod(method), url);
                    HttpResponseMessage serverRes = await Globals.httpClient.SendAsync(forwardRes);

                    //Response content 
                    Globals.start = DateTime.Now;
                    byte[] responseBytes = await serverRes.Content.ReadAsByteArrayAsync();
                    Globals.end = DateTime.Now;

                    Console.WriteLine($"[TIMING] {url} took {(Globals.end - Globals.start).TotalMilliseconds} ms");

                    if (responseBytes != null){
                        Globals.cache[url] = (DateTime.Now, responseBytes);
                    }

                    //Send response headers
                    foreach (var header in serverRes.Headers){
                        forwardRes.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        writer.WriteLine($"{header.Key}: {string.Join(",", header.Value)}");
                        writer.WriteLine();
                    }

                    //Send response body
                    await clientStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                }
                catch (Exception ex){
                    Console.WriteLine($"Error forwarding request: {ex.Message}");
                    writer.WriteLine("HTTP/1.1 500 Internal Server Error");
                    writer.WriteLine("Content-Type: text/plain");
                    writer.WriteLine();
                    writer.WriteLine("Proxy server error");
                }
                ClosingClient(client);
            }
        }

        static string Normalize(string url){
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)){
                Console.WriteLine("Invalid URL Format");
                return url;
            }
            if ((uri.Scheme == "http" && uri.Port == 80) || (uri.Scheme == "https" && uri.Port == 443))
            {
                return $"{uri.Scheme}://{uri.Host}{uri.PathAndQuery}";
            }
            return uri.ToString();
        }   

        static void ConsoleCommand(string command){
            if (command.ToUpper() == "list" || command == "l" ){
                if (Globals.blockedURLS.Count == 0){
                    Console.WriteLine("Empty List of Blocked URLs");
                    return;
                }
                Console.WriteLine("List of Blocked URLs:");
                foreach (string blockedUrl in Globals.blockedURLS){
                    Console.WriteLine(blockedUrl);
                }
                return;
            }
            string[] parts = command.Split(" ", 2);

            if (parts.Length < 2){
                Console.WriteLine("Invalid command. Use: block/b [URL] or unblock/u [URL]");
                return;
            }

            string action = parts[0].ToLower();
            string url = parts[1];

            if (action.ToLower() == "block" || action.ToLower() == "b" ){
                Globals.blockedURLS.Add(url);
                Console.WriteLine($"{url} Blocked");
            }
            else if (action.ToLower()  == "unblock" || action.ToLower()  == "u"){ 
                Globals.blockedURLS.Remove(url);
                Console.WriteLine($"{url} Unblocked");
            }
            else{
                Console.WriteLine("Invalid Command");
            }

        }
    }
}
