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