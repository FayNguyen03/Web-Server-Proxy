# Web-Server-Proxy

- Compile and run the program (without warning):

```{csharp}
dotnet build
dotnet run --property WarningLevel=0
```

- Run the server: using Netcat command to connect to `localhost:3000` and send a manual HTTP request

```
nc localhost 3000

GET http://example.com/ HTTP/1.1
Host: example.com 
```
Or type in the browser `localhost:4000/https://example.com/`


