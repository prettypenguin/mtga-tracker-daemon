using System;
using HackF5.UnitySpy;

namespace MTGATrackerDaemon.Controllers
{
    public class InventoryController
    {
        private readonly HttpServer _server;

        public InventoryController(HttpServer server)
        {
            _server = server;
        }

        public string HandleRequest()
        {
            try
            {
                DateTime startTime = DateTime.Now;
                IAssemblyImage assemblyImage = _server.CreateAssemblyImage();
                var inventory = assemblyImage["WrapperController"]["<Instance>k__BackingField"]["<InventoryManager>k__BackingField"]["_inventoryServiceWrapper"]["m_inventory"];

                TimeSpan ts = (DateTime.Now - startTime);
                return $"{{ \"gems\":{inventory["gems"]}, \"gold\":{inventory["gold"]}, \"elapsedTime\":{(int)ts.TotalMilliseconds} }}";
            }
            catch (Exception ex)
            {
                return $"{{\"error\":\"{ex.ToString()}\"}}";
            }
        }
    }
}