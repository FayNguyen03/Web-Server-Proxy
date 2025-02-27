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
    internal class ManagementConsole
    {
        
        public void Run()
        {
            while (true)
            {
                Console.WriteLine("\nAvailable Commands - Enter URL commands (block - B, unblock - U, list - L): ");
                string command = Console.ReadLine();
                if (!string.IsNullOrEmpty(command))
                {
                    ConsoleCommand(command);
                }
            }
        }

        void ConsoleCommand(string command){
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
