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
            
                bool cachedUrl = await CacheFetching(method, url, client, clientStream);
                if (cachedUrl){
                    return;
                }

                //Handle HTTPS CONNECT requests
                if ((method.ToUpper() == "CONNECT" || method.ToUpper() == "GET") && url[4] == 's'){
                    await HandleHttpsConnect(url, clientStream, writer);
                }
                Console.WriteLine("Done with HTTPS Connect");
                //Forward request to the actual web server
                await ForwardRequestToServer(method, url, clientStream, writer);
                Console.WriteLine("Done with HTTP Request");
            }   
            catch (Exception ex){
                Console.WriteLine($"[ERROR] {ex.Message}");
            }
            finally{
                ClosingClient(client);
            }
        }
             
        private static async Task HandleHttpsConnect(string url, NetworkStream clientStream, StreamWriter writer){
            try{

                //Remove the scheme
                if (url.StartsWith("https://"))
                {
                    url = url.Substring(8);
                }
                else if (url.StartsWith("http://"))
                {
                    url = url.Substring(7);
                }

                string[] hostParts = url.Split(":");

                string host = hostParts[0].Split('/')[0];
                int port = hostParts.Length > 2 ? int.Parse(hostParts[1]) : 443;

                Console.WriteLine($"[DEBUG] Host: {host}, Port: {port}");
                TcpClient server = new TcpClient(host, port);
                NetworkStream serverStream = server.GetStream();
                {
                    // Send "200 Connection Established" response to the client
                    await writer.WriteLineAsync("HTTP/1.1 200 Connection Established");
                    //await writer.WriteLineAsync("Proxy-Agent: C# Proxy");
                    await writer.WriteLineAsync(); 

                    // Relay encrypted traffic; Ensure both directions (client -> server and server -> client_ run) until the connection closes)
                    await Task.WhenAll(
                        clientStream.CopyToAsync(serverStream),
                        serverStream.CopyToAsync(clientStream)
                    );

                }
                Console.WriteLine($"[DEBUG] HTTPS CONNECT succeeded");
            }
            catch(Exception ex){
                Console.WriteLine($"[ERROR] HTTPS CONNECT failed: {ex.Message}");
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

                Console.WriteLine($"[TIMING] {url} took {(Globals.end - Globals.start).TotalMilliseconds} ms");

                if (responseBytes != null){
                    Globals.cache[url] = (DateTime.Now, responseBytes);
                    Console.WriteLine("Add to Cache");
                }

                //Send response headers
                foreach (var header in serverRes.Headers){
                   
                    await writer.WriteLineAsync($"{header.Key}: {string.Join(", ", header.Value)}");
                }
                
                Console.WriteLine("Done with the header");
                await writer.WriteLineAsync();
                await writer.FlushAsync();

                Console.WriteLine("Start sending the response body");
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
        }     

        static async Task<bool> CacheFetching(string method, string url, TcpClient client, NetworkStream clientStream){
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
                        return true;
                    }
                    else{
                        Globals.cache.Remove(url);
                    }
                }
                return false;
        }
        
        static async Task ClosingClient(TcpClient client){
            await Task.Delay(TimeSpan.FromMinutes(2));
            Console.WriteLine($"\nClose the Client");
            client.Close();
        }
    }
}
