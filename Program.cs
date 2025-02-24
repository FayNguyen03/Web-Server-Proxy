using System;
using System.Threading;

namespace WebProxy
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Create instances of both components.
            ProxyServer proxyServer = new ProxyServer();
            ManagementConsole managementConsole = new ManagementConsole();

            // Start the proxy server in a separate thread.
            Thread proxyThread = new Thread(new ThreadStart(proxyServer.StartProxy));
            proxyThread.Start();

            // Run the management console for URL commands on the main thread.
            managementConsole.Run();
        }
    }
}
