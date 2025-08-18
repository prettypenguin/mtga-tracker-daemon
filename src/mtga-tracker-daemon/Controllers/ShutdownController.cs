using System;

namespace MTGATrackerDaemon.Controllers
{
    public class ShutdownController
    {
        private readonly HttpServer _server;

        public ShutdownController(HttpServer server)
        {
            _server = server;
        }

        public string HandleRequest()
        {
            Console.WriteLine("Shutdown requested");
            _server.SetRunServer(false);
            return "{\"result\":\"shutdown request accepted\"}";
        }
    }
}