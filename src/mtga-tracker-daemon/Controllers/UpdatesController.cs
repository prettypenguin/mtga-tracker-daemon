using System;

namespace MTGATrackerDaemon.Controllers
{
    public class UpdatesController
    {
        private readonly HttpServer _server;

        public UpdatesController(HttpServer server)
        {
            _server = server;
        }

        public string HandleRequest()
        {
            bool updatesAvailable = _server.CheckForUpdates();
            return $"{{\"updatesAvailable\":\"{updatesAvailable.ToString().ToLower()}\"}}";
        }
    }
}