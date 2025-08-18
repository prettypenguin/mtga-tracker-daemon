using System;
using System.Diagnostics;

namespace MTGATrackerDaemon.Controllers
{
    public class StatusController
    {
        private readonly HttpServer _server;

        public StatusController(HttpServer server)
        {
            _server = server;
        }

        public string HandleRequest()
        {
            Process mtgaProcess = _server.GetMTGAProcess();
            if (mtgaProcess == null)
            {
                return $"{{\"isRunning\":\"false\", \"daemonVersion\":\"{_server.GetCurrentVersion()}\", \"updating\":\"{_server.GetUpdating().ToString().ToLower()}\", \"processId\":-1}}";
            }
            else
            {
                return $"{{\"isRunning\":\"true\", \"daemonVersion\":\"{_server.GetCurrentVersion()}\", \"updating\":\"{_server.GetUpdating().ToString().ToLower()}\", \"processId\":{mtgaProcess.Id}}}";
            }
        }
    }
}