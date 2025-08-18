using System;
using HackF5.UnitySpy;
using HackF5.UnitySpy.Detail;

namespace MTGATrackerDaemon.Controllers
{
    public class PlayerController
    {
        private readonly HttpServer _server;

        public PlayerController(HttpServer server)
        {
            _server = server;
        }

        public string HandleRequest()
        {
            try
            {
                DateTime startTime = DateTime.Now;
                IAssemblyImage assemblyImage = _server.CreateAssemblyImage();
                ManagedClassInstance accountInfo = (ManagedClassInstance) assemblyImage["WrapperController"]["<Instance>k__BackingField"]["<AccountClient>k__BackingField"]["<AccountInformation>k__BackingField"];

                string playerId = accountInfo.GetValue<string>("AccountID");
                string displayName = accountInfo.GetValue<string>("DisplayName");
                string personaId = accountInfo.GetValue<string>("PersonaID");
                TimeSpan ts = (DateTime.Now - startTime);
                return $"{{ \"playerId\":\"{playerId}\", \"displayName\":\"{displayName}\", \"personaId\":\"{personaId}\", \"elapsedTime\":{(int)ts.TotalMilliseconds} }}";
            }                    
            catch (Exception ex)
            {
                return $"{{\"error\":\"{ex.ToString()}\"}}";
            }
        }
    }
}