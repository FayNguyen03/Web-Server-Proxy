# Web-Server-Proxy

- Compile and run the program:

```{csharp}
dotnet build
dotnet run
```

- Run the server: using Netcat command to connect to `localhost:3000` and send a manual HTTP request

```
nc localhost 3000

GET http://example.com/ HTTP/1.1
Host: example.com 
```


```{csharp}
static void StartProxy() {
    // Accept client connections
    TcpListener listener = new TcpListener(IPAddress.Any, Globals.PORT_NUMBER);
    listener.Start();
    Console.WriteLine($"Proxy Server started on port {Globals.PORT_NUMBER}");

    while (true) {
        TcpClient client = listener.AcceptTcpClient();
        Task.Run(() => HandleClient(client));
    }
}
```

- Create a TCP Listener to accept client connections

- Use an infinite loop to accept multiple client connections

- Each connection handled asynchronously using `Task.Run()` to process multiple requests concurrently

