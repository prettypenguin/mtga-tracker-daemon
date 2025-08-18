using System;
using System.Text;
using HackF5.UnitySpy;
using HackF5.UnitySpy.Detail;

namespace MTGATrackerDaemon.Controllers
{
    public class EventsController
    {
        private readonly HttpServer _server;

        public EventsController(HttpServer server)
        {
            _server = server;
        }

        public string HandleRequest()
        {
            try
            {
                DateTime startTime = DateTime.Now;
                IAssemblyImage assemblyImage = _server.CreateAssemblyImage();
                object[] events = assemblyImage["PAPA"]["_instance"]["_eventManager"]["_eventsServiceWrapper"]["_cachedEvents"]["_items"];

                StringBuilder eventsArrayJSON = new StringBuilder("[");
                
                bool firstEvent = true;
                for (int i = 0; i < events.Length; i++)
                {
                    if(events[i] is ManagedClassInstance eventInstance)
                    {
                        string eventId = eventInstance.GetValue<string>("InternalEventName");
                        if (firstEvent)
                        {
                            firstEvent = false;
                        }
                        else
                        {
                            eventsArrayJSON.Append(",");
                        }
                        eventsArrayJSON.Append($"\"{eventId}\"");
                    }
                }
            
                eventsArrayJSON.Append("]");

                TimeSpan ts = (DateTime.Now - startTime);
                return $"{{\"events\":{eventsArrayJSON},\"elapsedTime\":{(int)ts.TotalMilliseconds}}}";
            }
            catch (Exception ex)
            {
                return $"{{\"error\":\"{ex.ToString()}\"}}";
            }
        }
    }
}