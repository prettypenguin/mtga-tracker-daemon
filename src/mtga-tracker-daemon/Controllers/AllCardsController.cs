using System;
using System.Text;
using Microsoft.Data.Sqlite;
using HackF5.UnitySpy;

namespace MTGATrackerDaemon.Controllers
{
    public class AllCardsController
    {
        private readonly HttpServer _server;

        public AllCardsController(HttpServer server)
        {
            _server = server;
        }

        public string HandleRequest()
        {
            try
            {
                DateTime startTime = DateTime.Now;
                IAssemblyImage assemblyImage = _server.CreateAssemblyImage();
                
                string connectionString = assemblyImage["WrapperController"]["<Instance>k__BackingField"]["<CardDatabase>k__BackingField"]["<CardDataProvider>k__BackingField"]["_baseCardDataProvider"]["_dbConnection"]["_connectionString"];
                
                StringBuilder cardsJSON = new StringBuilder();
                cardsJSON.Append("{\"cards\":[");
                
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    
                    var cardsCommand = connection.CreateCommand();
                    cardsCommand.CommandText = "SELECT c.GrpId, l.Loc as Title FROM Cards c JOIN Localizations_enUS l ON c.TitleId = l.LocId ORDER BY c.GrpId;";
                    
                    bool firstCard = true;
                    using (var reader = cardsCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!firstCard) cardsJSON.Append(",");
                            firstCard = false;
                            
                            int grpId = reader.GetInt32(0);
                            string title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            
                            cardsJSON.Append($"{{\"grpId\":{grpId},\"title\":\"{_server.JsonEscape(title)}\"}}");
                        }
                    }
                }
                
                cardsJSON.Append("]");
                
                TimeSpan ts = (DateTime.Now - startTime);
                cardsJSON.Append($",\"elapsedTime\":{(int)ts.TotalMilliseconds}}}");
                return cardsJSON.ToString();
            }
            catch (Exception ex)
            {
                return $"{{\"error\":\"{_server.JsonEscape(ex.ToString())}\"}}";
            }
        }
    }
}