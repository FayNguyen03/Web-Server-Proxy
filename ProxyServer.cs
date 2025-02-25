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
    internal class ProxyServer
    {

        public void StartProxy()
        {
            
            //accept client connections
            TcpListener listener = new TcpListener(IPAddress.Any, Globals.PORT_NUMBER);
            listener.Start();
            Console.WriteLine($"Proxy Server started on port {Globals.PORT_NUMBER}");

            while(true){
                TcpClient client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(state => HandleClient(client));
                //Using threadpool can be more efficient since this reuses threads instead of creating a new thread/request
                //Thread thread = new Thread(() => HandleClient(client));
                //thread.Start();
                
            }
        }

        static async Task HandleClient(TcpClient client){
            try{ 
                NetworkStream clientStream = client.GetStream();
                StreamReader reader = new StreamReader(clientStream, Globals.ascii);
                StreamWriter writer = new StreamWriter(clientStream, new UTF8Encoding(false)) {AutoFlush = true};

                //Read the HTTP request
                string requestLine = await reader.ReadLineAsync();
                
                //Request is empty
                if (string.IsNullOrEmpty(requestLine)){
                    ClosingClient(client);
                    return;
                }

                Console.WriteLine($"\nRequest Received: {requestLine}");

                //Parse method and URL
                string[] requestParts = requestLine.Split(' ');
                if (requestParts.Length < 2){
                    Console.WriteLine("Invalid Request");
                    ClosingClient(client);
                    return;
                }
      
                string method = requestParts[0];
                string url = requestParts[1];

                if (url[0] == '/'){
                    url = url.Remove(0,1);
                }

                //Blocked URLs requested
                if (Globals.blockedURLS.Contains(url)){
                    Console.WriteLine($"Urgh Oh! {url} is blocked!");
                    string response = "HTTP/1.1 403 Forbidden";
                    //Convert the blocking message into bytes
                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);    
                    await clientStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    ClosingClient(client);
                    return;
                }

                //Print out method and URL on the console
                Console.WriteLine($"Method: {method}");
                Console.WriteLine($"URL: {url}");
            
                bool cachedUrl = await CacheFetching(method, url, writer, clientStream);
                if (cachedUrl){
                    ClosingClient(client);
                    Console.WriteLine("[DEBUG] Cached Data Successfully!");
                    return;
                }

                //Handle HTTPS CONNECT requests
                if (url.StartsWith("https://")){
                    await HandleHttpsConnect(url, clientStream, writer, client);
                    await ForwardRequestToServer(method, url, clientStream, writer);
                }
                else if (url.StartsWith("http://")){
                //Forward request to the actual web server
                    await ForwardRequestToServer(method, url, clientStream, writer);
                }
                else{
                    Console.WriteLine("[ERROR] Unsupported HTTP method.");
                    await writer.WriteLineAsync("HTTP/1.1 405 Method Not Allowed\r\n\r\n");
                }
            }   
            catch (Exception ex){
                Console.WriteLine($"[ERROR] {ex.Message}");
            }
            finally{
                ClosingClient(client);
            }
        }
             
        private static async Task HandleHttpsConnect(string url, NetworkStream clientStream, StreamWriter writer, TcpClient client)
        {
            try
            {
                //Console.WriteLine($"[DEBUG] {url}");
                
                string newUrl = url.Remove(0,8);
                //Console.WriteLine($"[DEBUG] {newUrl}");
                string[] hostParts = newUrl.Split("/");
            
                string host = hostParts[0]; 

                int port = 443; // Default to 443 for HTTPS

                Console.WriteLine($"[DEBUG] Extracted Host: {host}, Port: {port}");

                
                using (TcpClient server = new TcpClient())
                {
                    try
                    {
                        await server.ConnectAsync(host, port);
                        using (NetworkStream serverStream = server.GetStream())
                        {
                            await writer.WriteLineAsync("HTTP/1.1 200 Connection Established");
                            await writer.FlushAsync();
                            Console.WriteLine($"[INFO] HTTPS Tunnel Established to {host}:{port}");
                            await ManualPipeServerToClient(serverStream, clientStream, writer);
                        }
                    }
                    catch (SocketException se)
                    {
                        Console.WriteLine($"[ERROR] Could not connect to {host}: {se.Message}");
                        await writer.WriteLineAsync("HTTP/1.1 502 Bad Gateway");
                    }
                }

                Console.WriteLine($"[DEBUG] HTTPS CONNECT succeeded");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] HTTPS CONNECT failed: {ex.Message}");
                await writer.WriteLineAsync("HTTP/1.1 502 Bad Gateway");
                await writer.WriteLineAsync("Content-Type: text/plain");
                await writer.WriteLineAsync();
                await writer.WriteLineAsync("Proxy server error: Unable to connect to target server.");
            }
        }
               
        private static async Task ForwardRequestToServer(string method, string url, NetworkStream clientStream, StreamWriter writer){
            try{
                HttpRequestMessage forwardRes = new HttpRequestMessage(new HttpMethod(method), url);
                HttpResponseMessage serverRes = await Globals.httpClient.SendAsync(forwardRes);

                //Response content 
                Globals.start = DateTime.Now;
                byte[] responseBytes = await serverRes.Content.ReadAsByteArrayAsync();
                Globals.end = DateTime.Now;

                Console.WriteLine($"[TIMING] {url} took {(Globals.end - Globals.start).TotalMilliseconds} ms to fetch from the server.");

                if (responseBytes != null && url.StartsWith("http://")){
                    Globals.cache[url] = (DateTime.Now, responseBytes);
                    Console.WriteLine("[DEBUG] Add to Cache");
                }

                await pasteResponse(responseBytes, serverRes, clientStream, writer);

                }
            catch (Exception ex){
                Console.WriteLine($"Error forwarding request: {ex.Message}");
                writer.WriteLine("HTTP/1.1 500 Internal Server Error");
                writer.WriteLine("Content-Type: text/plain");
                writer.WriteLine();
                writer.WriteLine("Proxy server error");
            }
        }     
    
        private static async Task pasteResponse(byte[] responseBytes, HttpResponseMessage serverRes, NetworkStream clientStream, StreamWriter writer)
        {
            try
            {
                // **Send HTTP status line**
                await writer.WriteLineAsync($"HTTP/{serverRes.Version} {(int)serverRes.StatusCode} {serverRes.ReasonPhrase}");
                if (serverRes.Content.Headers.ContentType != null)
                {
                    await writer.WriteLineAsync($"Content-Type: {serverRes.Content.Headers.ContentType}");
                }
                //Send response headers
                foreach (var header in serverRes.Headers){
                   
                    await writer.WriteLineAsync($"{header.Key}: {string.Join(", ", header.Value)}");
                }

                await writer.WriteLineAsync();
                await writer.FlushAsync();

                await clientStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending response to the client server: {ex.Message}");
                await writer.WriteLineAsync("HTTP/1.1 500 Internal Server Error");
                await writer.WriteLineAsync("Content-Type: text/plain");
                await writer.WriteLineAsync();
                await writer.WriteLineAsync("Proxy server error");
            }
        }
        
        static async Task<bool> CacheFetching(string method, string url, StreamWriter writer, NetworkStream clientStream){
            //Check Cache before Fetching
            HttpRequestMessage forwardRes = new HttpRequestMessage(new HttpMethod(method), url);
            HttpResponseMessage serverRes = await Globals.httpClient.SendAsync(forwardRes);

                if (method == "GET" && Globals.cache.ContainsKey(url)){
                    Globals.start = DateTime.Now;
                    var (timestamp, cachedData) = Globals.cache[url];

                    //Expire cache after 1 minute
                    if((DateTime.Now - timestamp).TotalMinutes < 10){
                        
                        Console.WriteLine($"[CACHE HIT] {url}");
                        await pasteResponse(cachedData, serverRes, clientStream, writer);
                        Globals.end = DateTime.Now;
                        Console.WriteLine($"[CACHE TIMING] {url} took {(Globals.end - Globals.start).TotalMilliseconds} ms");
                        return true;
                    }
                    else{
                        Globals.cache.Remove(url);
                    }
                }
                return false;
        }
        
        static async Task ClosingClient(TcpClient client){
            await Task.Delay(TimeSpan.FromMilliseconds(2));
            Console.WriteLine($"\nClose the Client");
            client.Close();
        }

        private static HttpResponseMessage ParseHttpResponse(byte[] responseBytes)
        {
            using (var memoryStream = new MemoryStream(responseBytes))
            using (var reader = new StreamReader(memoryStream))
            {
                string statusLine = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(statusLine))
                {
                    throw new Exception("Invalid response from server.");
                }

                // Parse status line (e.g., HTTP/1.1 200 OK)
                string[] statusParts = statusLine.Split(' ');
                if (statusParts.Length < 3)
                {
                    throw new Exception("Malformed status line in server response.");
                }

                HttpResponseMessage response = new HttpResponseMessage
                {
                    StatusCode = (HttpStatusCode)int.Parse(statusParts[1]),
                    Version = new Version(1, 1) // Default to HTTP/1.1
                };

                // Read headers
                while (true)
                {
                    string headerLine = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(headerLine))
                        break; // End of headers

                    string[] headerParts = headerLine.Split(new[] { ':' }, 2);
                    if (headerParts.Length == 2)
                    {
                        response.Headers.TryAddWithoutValidation(headerParts[0].Trim(), headerParts[1].Trim());
                    }
                }

                return response;
            }
        }

        private static async Task ManualPipeServerToClient(NetworkStream serverStream, NetworkStream clientStream, StreamWriter writer)
        {
            const int BUFFER_SIZE = 65536;
            byte[] buffer = new byte[BUFFER_SIZE];
            MemoryStream responseStream = new MemoryStream();
            int bytesRead;
            
            serverStream.ReadTimeout = 10000; 
            try
            {
                // Read from the server and store data
                while ((bytesRead = await serverStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    responseStream.Write(buffer, 0, bytesRead);
                }

                
                byte[] responseBytes = responseStream.ToArray();

                HttpResponseMessage serverRes = ParseHttpResponse(responseBytes);

                await pasteResponse(responseBytes, serverRes, clientStream, writer);
            }
            catch (IOException ioex)
            {
                Console.WriteLine($"[ERROR] Connection closed unexpectedly: {ioex.Message}");
            }
        }

    }
}