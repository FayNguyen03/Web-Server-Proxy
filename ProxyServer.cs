using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.ComponentModel.DataAnnotations;


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
                using(NetworkStream clientStream = client.GetStream())
                using(StreamReader reader = new StreamReader(clientStream, Globals.ascii))
                using(StreamWriter writer = new StreamWriter(clientStream, new UTF8Encoding(false)) {AutoFlush = true})
                {
                //Read the HTTP request
                string requestLine = await reader.ReadLineAsync();
                
                Console.WriteLine($"[INFO]{requestLine}");
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
      
                //string method =  requestParts[0];
                string url = requestParts[1];

                string method =  requestParts[0];

                if (url[0] == '/'){
                    url = url.Remove(0,1);
                }
                if(!(url.StartsWith("http://") ||  url.StartsWith("https://")) ){
                    url = Globals.lastHost + "/" + url;
                }
                else{
                    Uri uri = new Uri(url);
                    Globals.lastHost = uri.Scheme + "://" + uri.Host;
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
                await Task.Delay(100);
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
                
                string newUrl = url.Remove(0,8);
                
                string[] hostParts = newUrl.Split("/");
            
                string host = hostParts[0]; 

                int port = 443; 

                Console.WriteLine($"[DEBUG] Extracted Host: {host}, Port: {port}");

                
                using (TcpClient server = new TcpClient())
                {
                    try
                    {
                        await server.ConnectAsync(host, port);
                        using (NetworkStream serverStream = server.GetStream())
                        {
                            await writer.WriteLineAsync("HTTP/1.1 200 Connection Established\r\n\r\n");
                            await writer.FlushAsync();
                            Console.WriteLine($"[INFO] HTTPS Tunnel Established to {host}:{port}");
                            await Task.WhenAny(PipeStream(clientStream, serverStream), PipeStream(serverStream, clientStream));
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
                    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    {
                        url = "http://" + Globals.lastHost + url; 
                    }
                    else{
                        Uri uri = new Uri(url);
                        Globals.lastHost = uri.Scheme + "://" + uri.Host; 
                    }
                HttpRequestMessage forwardRes = new HttpRequestMessage(new HttpMethod(method), url);
                forwardRes.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.102 Safari/537.36");
                forwardRes.Headers.Add("Accept", "*/*");
                forwardRes.Headers.Add("Connection", "keep-alive");
                forwardRes.Headers.Referrer = new Uri(url);
                HttpResponseMessage serverRes = await Globals.httpClient.SendAsync(forwardRes);

                //Response content 
                Globals.start = DateTime.Now;
                byte[] responseBytes = await serverRes.Content.ReadAsByteArrayAsync();
                Globals.end = DateTime.Now;

                Console.WriteLine($"[TIMING] {url} took {(Globals.end - Globals.start).TotalMilliseconds} ms to fetch from the server.");

                if (responseBytes != null){
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
                
                await writer.WriteLineAsync($"HTTP/{serverRes.Version} {(int)serverRes.StatusCode} {serverRes.ReasonPhrase}");

                
                if (serverRes.Content.Headers.ContentType != null)
                {
                    await writer.WriteLineAsync($"Content-Type: {serverRes.Content.Headers.ContentType}");
                }
            

                foreach (var header in serverRes.Headers)
                {
                    await writer.WriteLineAsync($"{header.Key}: {string.Join(", ", header.Value)}");
                }

                
                if (serverRes.Headers.TransferEncodingChunked.HasValue && serverRes.Headers.TransferEncodingChunked.Value)
                {
                    await writer.WriteLineAsync("Transfer-Encoding: chunked");
                }

                await writer.WriteLineAsync();
                await writer.FlushAsync();

                if (serverRes.Headers.TransferEncodingChunked.HasValue && serverRes.Headers.TransferEncodingChunked.Value)
                {
                    using (var responseStream = await serverRes.Content.ReadAsStreamAsync())
                    {
                        byte[] buffer = new byte[65536]; 
                        int bytesRead;
                        
                        while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            string chunkSize = $"{bytesRead:X}\r\n"; 
                            await writer.WriteLineAsync(chunkSize);
                            await clientStream.WriteAsync(buffer, 0, bytesRead);
                            await writer.WriteLineAsync("\r\n"); 
                        }

                        // **Step 7: Send final empty chunk (signaling end of chunked response)**
                        await writer.WriteLineAsync("0\r\n\r\n");
                    }
                }
                else
                {
                   
                    await clientStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    await clientStream.FlushAsync();
                }

                Console.WriteLine($"[INFO] Response forwarded successfully. Content-Type: {serverRes.Content.Headers.ContentType}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Error sending response: {ex.Message}");
                await writer.WriteLineAsync("HTTP/1.1 500 Internal Server Error\r\n\r\n");
                await writer.FlushAsync();
            }
        }

        
        static async Task<bool> CacheFetching(string method, string url, StreamWriter writer, NetworkStream clientStream){
            //Check Cache before Fetching
            if (method == "CONNECT"){
                return false;
            }
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
                        Globals.cache.TryRemove(url, out var temp);
                    }
                }
                return false;
        }
        
        static async Task ClosingClient(TcpClient client){
            await Task.Delay(TimeSpan.FromMilliseconds(2));
            Console.WriteLine($"\nClose the Client");
            client.Close();
        }

        private static async Task PipeStream(NetworkStream source, NetworkStream destination)
        {
            byte[] buffer = new byte[65536*2]; // Small buffer for efficient streaming
            Console.WriteLine("[INFO] PipeStream is running.");
            int bytesRead = 0;
            bytesRead = await source.ReadAsync(buffer, 0, buffer.Length);
            Console.WriteLine(bytesRead);
            try
            {
                while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    Console.WriteLine("[INFO] Getting Information.");
                    await destination.WriteAsync(buffer, 0, bytesRead);
                    await destination.FlushAsync();
                }
            }
            catch (IOException)
            {
                Console.WriteLine("[INFO] Connection closed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Streaming error: {ex.Message}");
            }
        }
    }
}