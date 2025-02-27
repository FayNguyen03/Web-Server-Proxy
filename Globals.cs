using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Collections.Concurrent;


namespace WebProxy{
    public static class Globals
    {
        public const Int32 PORT_NUMBER = 4000;
        public static readonly HttpClient httpClient = new HttpClient();

        public static Encoding ascii = Encoding.ASCII;

        //blocked URL set
        public static HashSet<string> blockedURLS = new HashSet<string>();

        //public static Dictionary<string, (DateTime, byte[])> cache = new Dictionary<string, (DateTime, byte[])>();
        public static ConcurrentDictionary<string, (DateTime, byte[])> cache = new ConcurrentDictionary<string, (DateTime, byte[])>();


        public static DateTime start;

        public static DateTime end;

        public static string lastHost = "";
    }
    
}
