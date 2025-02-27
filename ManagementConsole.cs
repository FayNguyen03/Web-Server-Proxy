using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.IO;

namespace WebProxy
{
    internal class ManagementConsole
    {
        private static readonly HttpClient client = Globals.httpClient;

        public void Run()
        {
            while (true)
            {
                Console.WriteLine("\nCONSOLE MANAGEMENT (Click Enter to show the console)");
                Console.WriteLine("\nAvailable Commands: ");
                Console.WriteLine(" - Block URL (block/b [URL])");
                Console.WriteLine(" - Unblock URL (unblock/u [URL])");
                Console.WriteLine(" - List Blocked URLs (list/l)");
                Console.WriteLine("Enter Command: ");

                string command = Console.ReadLine();
                if (!string.IsNullOrEmpty(command))
                {
                    ConsoleCommand(command);
                }
            }
        }

        async void ConsoleCommand(string command)
        {
            string[] parts = command.Split(" ", 3, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                Console.WriteLine("Invalid command. Try again.");
                return;
            }

            string action = parts[0].ToLower();

            switch (action)
            {
                case "list":
                case "l":
                    ListBlockedURLs();
                    break;

                case "block":
                case "b":
                    if (parts.Length < 2)
                    {
                        //Console.WriteLine("Usage: block/b [URL]");
                        return;
                    }
                    BlockURL(parts[1]);
                    break;

                case "unblock":
                case "u":
                    if (parts.Length < 2)
                    {
                        //Console.WriteLine("Usage: unblock/u [URL]");
                        return;
                    }
                    UnblockURL(parts[1]);
                    break;

                
                default:
                    Console.WriteLine("Invalid command.");
                    break;
            }
        }

        void ListBlockedURLs()
        {
            if (Globals.blockedURLS.Count == 0)
            {
                Console.WriteLine("No blocked URLs.");
                return;
            }

            Console.WriteLine("Blocked URLs:");
            foreach (string url in Globals.blockedURLS)
            {
                Console.WriteLine($"- {url}");
            }
        }

        void BlockURL(string url)
        {
            Globals.blockedURLS.Add(url);
            Console.WriteLine($"{url} has been blocked.");
        }

        void UnblockURL(string url)
        {
            if (Globals.blockedURLS.Remove(url))
            {
                Console.WriteLine($"{url} has been unblocked.");
            }
            else
            {
                Console.WriteLine($"{url} was not in the blocked list.");
            }
        }

    }
}
