using System;
using System.Text;
using HackF5.UnitySpy;
using HackF5.UnitySpy.Detail;

namespace MTGATrackerDaemon.Controllers
{
    public class CardsController
    {
        private readonly HttpServer _server;

        public CardsController(HttpServer server)
        {
            _server = server;
        }

        public string HandleRequest()
        {
            try
            {
                DateTime startTime = DateTime.Now;
                IAssemblyImage assemblyImage = _server.CreateAssemblyImage();
                object[] cards = assemblyImage["WrapperController"]["<Instance>k__BackingField"]["<InventoryManager>k__BackingField"]["_inventoryServiceWrapper"]["<Cards>k__BackingField"]["_entries"];

                StringBuilder cardsArrayJSON = new StringBuilder("[");
                
                bool firstCard = true;
                for (int i = 0; i < cards.Length; i++)
                {
                    if(cards[i] is ManagedStructInstance cardInstance)
                    {
                        int owned = cardInstance.GetValue<int>("value");
                        if (owned > 0)
                        {
                            if (firstCard)
                            {
                                firstCard = false;
                            }
                            else
                            {
                                cardsArrayJSON.Append(",");
                            }
                            uint groupId = cardInstance.GetValue<uint>("key");
                            cardsArrayJSON.Append($"{{\"grpId\":{groupId}, \"owned\":{owned}}}");
                        }
                    }
                }

                cardsArrayJSON.Append("]");

                TimeSpan ts = (DateTime.Now - startTime);
                return $"{{ \"cards\":{cardsArrayJSON}, \"elapsedTime\":{(int)ts.TotalMilliseconds} }}";
            }                    
            catch (Exception ex)
            {
                return $"{{\"error\":\"{ex.ToString()}\"}}";
            }
        }
    }
}