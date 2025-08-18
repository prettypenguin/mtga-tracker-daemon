// Based on the work of Benjamin N. Summerton <define-private-public> on HttpServer.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Data.Sqlite;

using HackF5.UnitySpy;
using HackF5.UnitySpy.Detail;
using HackF5.UnitySpy.Offsets;
using HackF5.UnitySpy.ProcessFacade;

using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;

using Newtonsoft.Json;

namespace MTGATrackerDaemon
{
    public class HttpServer
    {
        private HttpListener listener;

        private bool runServer = true;

        private Version currentVersion;

        private bool updating = false;
            
        public void Start(string url)
        {
            var assembly = Assembly.GetExecutingAssembly();
            currentVersion = assembly.GetName().Version;
            Console.WriteLine($"Current version = {currentVersion}");

            CheckForUpdates();

            // Create a Http server and start listening for incoming connections
            listener = new HttpListener();
            listener.Prefixes.Add(url);
            listener.Start();
            Console.WriteLine("Listening for connections on {0}", url);

            // Handle requests
            Task listenTask = HandleIncomingConnections();
            listenTask.GetAwaiter().GetResult();

            // Close the listener
            listener.Close();
        }

        private async Task HandleIncomingConnections()
        {
            // While a user hasn't visited the `shutdown` url, keep on handling requests
            while (runServer)
            {
                try
                {
                    // Will wait here until we hear from a connection
                    HttpListenerContext ctx = await listener.GetContextAsync();

                    // Peel out the requests and response objects
                    HttpListenerRequest request = ctx.Request;
                    if(request.IsLocal)
                    {
                        await HandleRequest(request, ctx.Response);
                    }
                }
                catch (Exception)
                {

                }
            }
        }

        private async Task HandleRequest(HttpListenerRequest request, HttpListenerResponse response) {
            string responseJSON = "{\"error\":\"unsupported request\"}";

            // If `shutdown` url requested w/ POST, then shutdown the server after serving the page
            if (request.HttpMethod == "POST")
            {
                if (request.Url.AbsolutePath == "/shutdown")
                {
                    Console.WriteLine("Shutdown requested");
                    responseJSON = "{\"result\":\"shutdown request accepted\"}";
                    runServer = false;
                }
                else if(request.Url.AbsolutePath == "/checkForUpdates")
                {
                    bool updatesAvailable = CheckForUpdates();
                    responseJSON = $"{{\"updatesAvailable\":\"{updatesAvailable.ToString().ToLower()}\"}}";
                }
            } 
            else if (request.HttpMethod == "GET")
            {
                if (request.Url.AbsolutePath == "/status")
                {
                    Process mtgaProcess = GetMTGAProcess();
                    if (mtgaProcess == null)
                    {
                        responseJSON = $"{{\"isRunning\":\"false\", \"daemonVersion\":\"{currentVersion}\", \"updating\":\"{updating.ToString().ToLower()}\", \"processId\":-1}}";
                    }
                    else
                    {
                        responseJSON = $"{{\"isRunning\":\"true\", \"daemonVersion\":\"{currentVersion}\", \"updating\":\"{updating.ToString().ToLower()}\", \"processId\":{mtgaProcess.Id}}}";
                    }
                }
                else if (request.Url.AbsolutePath == "/cards")
                {
                    try
                    {
                        DateTime startTime = DateTime.Now;
                        IAssemblyImage assemblyImage = CreateAssemblyImage();
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
                        responseJSON = $"{{ \"cards\":{cardsArrayJSON}, \"elapsedTime\":{(int)ts.TotalMilliseconds} }}";
                    }                    
                    catch (Exception ex)
                    {
                        responseJSON = $"{{\"error\":\"{ex.ToString()}\"}}";
                    }      
                }
                else if (request.Url.AbsolutePath == "/playerId") 
                {
                    try
                    {
                        DateTime startTime = DateTime.Now;
                        IAssemblyImage assemblyImage = CreateAssemblyImage();
                        ManagedClassInstance accountInfo = (ManagedClassInstance) assemblyImage["WrapperController"]["<Instance>k__BackingField"]["<AccountClient>k__BackingField"]["<AccountInformation>k__BackingField"];

                        string playerId = accountInfo.GetValue<string>("AccountID");
                        string displayName = accountInfo.GetValue<string>("DisplayName");
                        string personaId = accountInfo.GetValue<string>("PersonaID");
                        TimeSpan ts = (DateTime.Now - startTime);
                        responseJSON = $"{{ \"playerId\":\"{playerId}\", \"displayName\":\"{displayName}\", \"personaId\":\"{personaId}\", \"elapsedTime\":{(int)ts.TotalMilliseconds} }}";
                    }                    
                    catch (Exception ex)
                    {
                        responseJSON = $"{{\"error\":\"{ex.ToString()}\"}}";
                    }
                }
                else if (request.Url.AbsolutePath == "/inventory")
                {
                    try
                    {
                        DateTime startTime = DateTime.Now;
                        IAssemblyImage assemblyImage = CreateAssemblyImage();
                        var inventory = assemblyImage["WrapperController"]["<Instance>k__BackingField"]["<InventoryManager>k__BackingField"]["_inventoryServiceWrapper"]["m_inventory"];

                        TimeSpan ts = (DateTime.Now - startTime);
                        responseJSON = $"{{ \"gems\":{inventory["gems"]}, \"gold\":{inventory["gold"]}, \"elapsedTime\":{(int)ts.TotalMilliseconds} }}";
                    }
                    catch (Exception ex)
                    {
                        responseJSON = $"{{\"error\":\"{ex.ToString()}\"}}";
                    }
                }
                else if (request.Url.AbsolutePath == "/events")
                {
                    try
                    {
                        DateTime startTime = DateTime.Now;
                        IAssemblyImage assemblyImage = CreateAssemblyImage();
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
                        responseJSON = $"{{\"events\":{eventsArrayJSON},\"elapsedTime\":{(int)ts.TotalMilliseconds}}}";

                    }
                    catch (Exception ex)
                    {
                        responseJSON = $"{{\"error\":\"{ex.ToString()}\"}}";
                    }
                }
                else if (request.Url.AbsolutePath == "/matchState")
                {
                    try
                    {
                        DateTime startTime = DateTime.Now;
                        IAssemblyImage assemblyImage = CreateAssemblyImage();
                        ManagedClassInstance matchManager = (ManagedClassInstance) assemblyImage["PAPA"]["_instance"]["_matchManager"];

                        string matchId = matchManager.GetValue<string>("<MatchID>k__BackingField");

                        ManagedClassInstance localPlayerInfo = (ManagedClassInstance) assemblyImage["PAPA"]["_instance"]["_matchManager"]["<LocalPlayerInfo>k__BackingField"];

                        float LocalMythicPercentile = localPlayerInfo.GetValue<float>("MythicPercentile");
                        int LocalMythicPlacement = localPlayerInfo.GetValue<int>("MythicPlacement");
                        int LocalRankingClass = localPlayerInfo.GetValue<int>("RankingClass");
                        int LocalRankingTier = localPlayerInfo.GetValue<int>("RankingTier");

                        ManagedClassInstance opponentInfo = (ManagedClassInstance) assemblyImage["PAPA"]["_instance"]["_matchManager"]["<OpponentInfo>k__BackingField"];

                        float OpponentMythicPercentile = opponentInfo.GetValue<float>("MythicPercentile");
                        int OpponentMythicPlacement = opponentInfo.GetValue<int>("MythicPlacement");
                        int OpponentRankingClass = opponentInfo.GetValue<int>("RankingClass");
                        int OpponentRankingTier = opponentInfo.GetValue<int>("RankingTier");
                   
                        TimeSpan ts = (DateTime.Now - startTime);
                        responseJSON = $"{{\"matchId\": \"{matchId}\",\"playerRank\":{{\"mythicPercentile\":{LocalMythicPercentile},\"mythicPlacement\":{LocalMythicPlacement},\"class\":{LocalRankingClass},\"tier\":{LocalRankingTier}}},\"opponentRank\":{{\"mythicPercentile\":{OpponentMythicPercentile},\"mythicPlacement\":{OpponentMythicPlacement},\"class\":{OpponentRankingClass},\"tier\":{OpponentRankingTier}}},\"elapsedTime\":{(int)ts.TotalMilliseconds}}}";
                    }
                    catch (Exception ex)
                    {
                        responseJSON = $"{{\"error\":\"{ex.ToString()}\"}}";
                    }
                }
                else if (request.Url.AbsolutePath == "/allcards")
                {
                    try
                    {
                        DateTime startTime = DateTime.Now;
                        IAssemblyImage assemblyImage = CreateAssemblyImage();
                        
                        string connectionString = assemblyImage["WrapperController"]["<Instance>k__BackingField"]["<CardDatabase>k__BackingField"]["<CardDataProvider>k__BackingField"]["_baseCardDataProvider"]["_dbConnection"]["_connectionString"];
                        
                        StringBuilder cardsJSON = new StringBuilder();
                        cardsJSON.Append("{\"cards\":[");
                        
                        using (var connection = new SqliteConnection(connectionString))
                        {
                            connection.Open();
                            
                            // Get all cards with their titles
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
                                    
                                    cardsJSON.Append($"{{\"grpId\":{grpId},\"title\":\"{JsonEscape(title)}\"}}");
                                }
                            }
                        }
                        
                        cardsJSON.Append("]");
                        
                        TimeSpan ts = (DateTime.Now - startTime);
                        cardsJSON.Append($",\"elapsedTime\":{(int)ts.TotalMilliseconds}}}");
                        responseJSON = cardsJSON.ToString();
                    }
                    catch (Exception ex)
                    {
                        responseJSON = $"{{\"error\":\"{JsonEscape(ex.ToString())}\"}}";
                    }
                }
                else if (request.Url.AbsolutePath.StartsWith("/explore"))
                {
                    try
                    {
                        DateTime startTime = DateTime.Now;
                        IAssemblyImage assemblyImage = CreateAssemblyImage();
                        
                        // Parse path parameter from query string
                        string path = request.QueryString["path"] ?? "";
                        string[] pathParts = string.IsNullOrEmpty(path) ? new string[0] : path.Split('|');
                        
                        // Navigate to the specified path
                        object currentObject = assemblyImage;
                        string currentPath = "";
                        
                        foreach (string part in pathParts)
                        {
                            if (!string.IsNullOrEmpty(part))
                            {
                                currentObject = GetObjectProperty(currentObject, part);
                                currentPath += (string.IsNullOrEmpty(currentPath) ? "" : "|") + part;
                            }
                        }
                        
                        string htmlResponse = GenerateExplorerHTML(currentObject, currentPath, request.Url.Authority);
                        
                        TimeSpan ts = (DateTime.Now - startTime);
                        
                        // Return HTML instead of JSON for this endpoint
                        byte[] htmlData = Encoding.UTF8.GetBytes(htmlResponse);
                        response.AddHeader("Access-Control-Allow-Origin", "*");
                        response.AddHeader("Access-Control-Allow-Methods", "*");
                        response.AddHeader("Access-Control-Allow-Headers", "*");
                        
                        response.ContentType = "text/html";
                        response.ContentEncoding = Encoding.UTF8;
                        response.ContentLength64 = htmlData.LongLength;
                        
                        await response.OutputStream.WriteAsync(htmlData, 0, htmlData.Length);
                        response.Close();
                        return;
                    }
                    catch (Exception ex)
                    {
                        responseJSON = $"{{\"error\":\"{JsonEscape(ex.ToString())}\"}}";
                    }
                }
            }        

            // Write the response info
            byte[] data = Encoding.UTF8.GetBytes(responseJSON);
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "*");
            response.AddHeader("Access-Control-Allow-Headers", "*");
            
            response.ContentType = "Application/json";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = data.LongLength;

            // Write out to the response stream (asynchronously), then close it
            await response.OutputStream.WriteAsync(data, 0, data.Length);
            response.Close();
        }

        private object GetObjectProperty(object obj, string propertyName)
        {
            if (obj == null) return null;
            
            if (obj is IAssemblyImage assemblyImage)
            {
                return assemblyImage[propertyName];
            }
            else if (obj is ITypeDefinition typeDef)
            {
                // For type definitions, try to get static fields or instance fields
                try
                {
                    // First try to get as static field value
                    return typeDef.GetStaticValue<object>(propertyName);
                }
                catch
                {
                    // If that fails, try to find an instance of this class and access the field from it
                    try
                    {
                        // Look for common singleton patterns
                        object instance = null;
                        
                        // Try to find Instance, _instance, instance fields
                        var instanceFieldNames = new[] { "Instance", "_instance", "instance", "<Instance>k__BackingField" };
                        foreach (var instanceFieldName in instanceFieldNames)
                        {
                            try
                            {
                                instance = typeDef.GetStaticValue<object>(instanceFieldName);
                                if (instance != null) break;
                            }
                            catch { }
                        }
                        
                        // If we found an instance, try to access the property from it
                        if (instance is IManagedObjectInstance managedInstance)
                        {
                            return managedInstance[propertyName];
                        }
                    }
                    catch { }
                    
                    // If all else fails, return the field definition for info
                    var field = typeDef.Fields.FirstOrDefault(f => f.Name == propertyName);
                    return field;
                }
            }
            else if (obj is IManagedObjectInstance managedObj)
            {
                return managedObj[propertyName];
            }
            else if (obj.GetType().IsArray)
            {
                Array array = (Array)obj;
                if (int.TryParse(propertyName, out int index) && index >= 0 && index < array.Length)
                {
                    return array.GetValue(index);
                }
            }
            
            return null;
        }

        private string GenerateExplorerHTML(object currentObject, string currentPath, string authority)
        {
            StringBuilder html = new StringBuilder();
            html.Append("<!DOCTYPE html><html><head><title>MTGA Memory Explorer</title>");
            html.Append("<style>body{font-family:Arial,sans-serif;margin:20px;} .path{background:#f0f0f0;padding:10px;margin-bottom:20px;} ");
            html.Append(".property{margin:5px 0;} .clickable{color:blue;text-decoration:underline;cursor:pointer;} ");
            html.Append(".value{color:green;} .type{color:gray;font-style:italic;} .back{margin-bottom:20px;}</style></head><body>");
            
            html.Append("<h1>MTGA Memory Explorer</h1>");
            
            // Current path display
            html.Append($"<div class='path'><strong>Current Path:</strong> {(string.IsNullOrEmpty(currentPath) ? "Root" : currentPath.Replace("|", " → "))}</div>");
            
            // Back button
            if (!string.IsNullOrEmpty(currentPath))
            {
                string[] pathParts = currentPath.Split('|');
                string parentPath = string.Join("|", pathParts.Take(pathParts.Length - 1));
                html.Append($"<div class='back'><a href='http://{authority}/explore?path={Uri.EscapeDataString(parentPath)}'>← Back</a></div>");
            }
            
            html.Append("<div class='properties'>");
            
            try
            {
                if (currentObject is IAssemblyImage assemblyImage)
                {
                    html.Append("<h3>Assembly Image Properties:</h3>");
                    foreach (var typeDef in assemblyImage.TypeDefinitions.OrderBy(t => t.Name))
                    {
                        string newPath = string.IsNullOrEmpty(currentPath) ? typeDef.Name : currentPath + "|" + typeDef.Name;
                        html.Append($"<div class='property'><a class='clickable' href='http://{authority}/explore?path={Uri.EscapeDataString(newPath)}'>{HtmlEscape(typeDef.Name)}</a> <span class='type'>(Type)</span></div>");
                    }
                }
                else if (currentObject is ITypeDefinition typeDefinition)
                {
                    html.Append("<h3>Type Fields:</h3>");
                    foreach (var fieldDef in typeDefinition.Fields)
                    {
                        try
                        {
                            string newPath = string.IsNullOrEmpty(currentPath) ? fieldDef.Name : currentPath + "|" + fieldDef.Name;
                            
                            // Try to determine if this is a static field or instance field
                            string fieldInfo = fieldDef.TypeInfo.IsStatic ? " (Static)" : " (Instance)";
                            string typeName = fieldDef.TypeInfo.TryGetTypeDefinition(out var typeDef) ? typeDef.Name : fieldDef.TypeInfo.TypeCode.ToString();
                            
                            if (fieldDef.TypeInfo.IsStatic)
                            {
                                // For static fields, we can potentially navigate to them
                                string linkPath = GetSmartNavigationPath(newPath, fieldDef.Name);
                                html.Append($"<div class='property'><a class='clickable' href='http://{authority}/explore?path={Uri.EscapeDataString(linkPath)}'>{HtmlEscape(fieldDef.Name)}</a> <span class='type'>({HtmlEscape(typeName)}{fieldInfo})</span></div>");
                            }
                            else
                            {
                                // For instance fields, make them clickable too - we'll try to access them from any available instance
                                string linkPath = GetSmartNavigationPath(newPath, fieldDef.Name);
                                html.Append($"<div class='property'><a class='clickable' href='http://{authority}/explore?path={Uri.EscapeDataString(linkPath)}'>{HtmlEscape(fieldDef.Name)}</a> <span class='type'>({HtmlEscape(typeName)}{fieldInfo})</span></div>");
                            }
                        }
                        catch (Exception ex)
                        {
                            html.Append($"<div class='property'>{HtmlEscape(fieldDef.Name)}: <span style='color:red'>Error: {HtmlEscape(ex.Message)}</span></div>");
                        }
                    }
                }
                else if (currentObject is IManagedObjectInstance managedObj)
                {
                    html.Append("<h3>Object Properties:</h3>");
                    foreach (var fieldDef in managedObj.TypeDefinition.Fields)
                    {
                        try
                        {
                            var value = managedObj[fieldDef.Name];
                            string newPath = string.IsNullOrEmpty(currentPath) ? fieldDef.Name : currentPath + "|" + fieldDef.Name;
                            
                            if (value != null && (value is IManagedObjectInstance || value.GetType().IsArray))
                            {
                                string linkPath = GetSmartNavigationPath(newPath, fieldDef.Name);
                                html.Append($"<div class='property'><a class='clickable' href='http://{authority}/explore?path={Uri.EscapeDataString(linkPath)}'>{HtmlEscape(fieldDef.Name)}</a> <span class='type'>({HtmlEscape(value.GetType().Name)})</span></div>");
                            }
                            else
                            {
                                string valueStr = value?.ToString() ?? "null";
                                if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                                string typeName = fieldDef.TypeInfo.TryGetTypeDefinition(out var typeDef) ? typeDef.Name : fieldDef.TypeInfo.TypeCode.ToString();
                                html.Append($"<div class='property'>{HtmlEscape(fieldDef.Name)}: <span class='value'>{HtmlEscape(valueStr)}</span> <span class='type'>({HtmlEscape(typeName)})</span></div>");
                            }
                        }
                        catch (Exception ex)
                        {
                            html.Append($"<div class='property'>{HtmlEscape(fieldDef.Name)}: <span style='color:red'>Error: {HtmlEscape(ex.Message)}</span></div>");
                        }
                    }
                }
                else if (currentObject is IFieldDefinition fieldDefinition)
                {
                    html.Append("<h3>Field Definition:</h3>");
                    html.Append($"<div class='property'>Name: <span class='value'>{HtmlEscape(fieldDefinition.Name)}</span></div>");
                    string typeName = fieldDefinition.TypeInfo.TryGetTypeDefinition(out var typeDef) ? typeDef.Name : fieldDefinition.TypeInfo.TypeCode.ToString();
                    html.Append($"<div class='property'>Type: <span class='value'>{HtmlEscape(typeName)}</span></div>");
                    html.Append($"<div class='property'>Is Static: <span class='value'>{fieldDefinition.TypeInfo.IsStatic}</span></div>");
                    html.Append($"<div class='property'>Is Constant: <span class='value'>{fieldDefinition.TypeInfo.IsConstant}</span></div>");
                    html.Append($"<div class='property'>Declaring Type: <span class='value'>{HtmlEscape(fieldDefinition.DeclaringType.FullName)}</span></div>");
                }
                else if (currentObject != null && currentObject.GetType().IsArray)
                {
                    Array array = (Array)currentObject;
                    html.Append($"<h3>Array Elements (Length: {array.Length}):</h3>");
                    
                    int maxDisplay = Math.Min(array.Length, 50); // Limit display to first 50 elements
                    for (int i = 0; i < maxDisplay; i++)
                    {
                        try
                        {
                            var element = array.GetValue(i);
                            string newPath = string.IsNullOrEmpty(currentPath) ? i.ToString() : currentPath + "|" + i.ToString();
                            
                            if (element != null && (element is IManagedObjectInstance || element.GetType().IsArray))
                            {
                                html.Append($"<div class='property'><a class='clickable' href='http://{authority}/explore?path={Uri.EscapeDataString(newPath)}'>[{i}]</a> <span class='type'>({HtmlEscape(element.GetType().Name)})</span></div>");
                            }
                            else
                            {
                                string valueStr = element?.ToString() ?? "null";
                                if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                                html.Append($"<div class='property'>[{i}]: <span class='value'>{HtmlEscape(valueStr)}</span></div>");
                            }
                        }
                        catch (Exception ex)
                        {
                            html.Append($"<div class='property'>[{i}]: <span style='color:red'>Error: {HtmlEscape(ex.Message)}</span></div>");
                        }
                    }
                    
                    if (array.Length > maxDisplay)
                    {
                        html.Append($"<div class='property'><em>... and {array.Length - maxDisplay} more elements</em></div>");
                    }
                }
                else
                {
                    html.Append($"<div class='property'>Value: <span class='value'>{HtmlEscape(currentObject?.ToString() ?? "null")}</span></div>");
                    html.Append($"<div class='property'>Type: <span class='type'>{HtmlEscape(currentObject?.GetType().Name ?? "null")}</span></div>");
                    html.Append($"<div class='property'>Full Type: <span class='type'>{HtmlEscape(currentObject?.GetType().FullName ?? "null")}</span></div>");
                    
                    // If it's a managed object instance, try to show its properties
                    if (currentObject is IManagedObjectInstance debugManagedObj)
                    {
                        html.Append("<h3>Debug - Object Properties:</h3>");
                        foreach (var field in debugManagedObj.TypeDefinition.Fields)
                        {
                            try
                            {
                                var value = debugManagedObj[field.Name];
                                string valueStr = value?.ToString() ?? "null";
                                if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                                string newPath = string.IsNullOrEmpty(currentPath) ? field.Name : currentPath + "|" + field.Name;
                                
                                if (value != null && (value is IManagedObjectInstance || value.GetType().IsArray))
                                {
                                    html.Append($"<div class='property'><a class='clickable' href='http://{authority}/explore?path={Uri.EscapeDataString(newPath)}'>{HtmlEscape(field.Name)}</a> <span class='type'>({HtmlEscape(value.GetType().Name)})</span></div>");
                                }
                                else
                                {
                                    html.Append($"<div class='property'>{HtmlEscape(field.Name)}: <span class='value'>{HtmlEscape(valueStr)}</span></div>");
                                }
                            }
                            catch (Exception ex)
                            {
                                html.Append($"<div class='property'>{HtmlEscape(field.Name)}: <span style='color:red'>Error: {HtmlEscape(ex.Message)}</span></div>");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                html.Append($"<div style='color:red'>Error exploring object: {HtmlEscape(ex.Message)}</div>");
            }
            
            html.Append("</div></body></html>");
            return html.ToString();
        }

        private string GetSmartNavigationPath(string fullPath, string fieldName)
        {
            // Smart navigation for common patterns
            // fullPath already contains currentPath + "|" + fieldName
            
            // When clicking on managers from WrapperController's Instance, add the manager's Instance automatically
            if (fullPath.StartsWith("WrapperController|<Instance>k__BackingField|") && fieldName.EndsWith("k__BackingField"))
            {
                if (fieldName.Contains("Manager") || fieldName.Contains("Client") || fieldName.Contains("Database"))
                {
                    return fullPath + "|<Instance>k__BackingField";
                }
            }
            
            // Return the path as-is for other cases
            return fullPath;
        }

        private string HtmlEscape(string text)
        {
            if (text == null) return "null";
            return text.Replace("&", "&amp;")
                      .Replace("<", "&lt;")
                      .Replace(">", "&gt;")
                      .Replace("\"", "&quot;")
                      .Replace("'", "&#39;");
        }

        private IAssemblyImage CreateAssemblyImage()
        {
            UnityProcessFacade unityProcess = CreateUnityProcessFacade();
            return AssemblyImageFactory.Create(unityProcess, "Core");  
        }


        private string JsonEscape(string text)
        {
            if (text == null) return "null";
            return text.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\r", "\\r")
                      .Replace("\n", "\\n")
                      .Replace("\t", "\\t");
        }


    

        private UnityProcessFacade CreateUnityProcessFacade()
        {            
            Process mtgaProcess = GetMTGAProcess();
            if (mtgaProcess == null)
            {
                return null;
            }

            ProcessFacade processFacade; 
            MonoLibraryOffsets monoLibraryOffsets;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string memPseudoFilePath = $"/proc/{mtgaProcess.Id}/mem";
                ProcessFacadeLinuxDirect processFacadeLinux = new ProcessFacadeLinuxDirect(mtgaProcess.Id, memPseudoFilePath);
                string gameExecutableFilePath = processFacadeLinux.GetModulePath(mtgaProcess.ProcessName);
                processFacade = processFacadeLinux;
                monoLibraryOffsets = MonoLibraryOffsets.GetOffsets(gameExecutableFilePath);
            }
            else 
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))      
                {   
                    ProcessFacadeWindows processFacadeWindows = new ProcessFacadeWindows(mtgaProcess);
                    monoLibraryOffsets = MonoLibraryOffsets.GetOffsets(processFacadeWindows.GetMainModuleFileName());
                    processFacade = processFacadeWindows;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {   
                    processFacade = new ProcessFacadeMacOSDirect(mtgaProcess);
                    monoLibraryOffsets = MonoLibraryOffsets.GetOffsets(mtgaProcess.MainModule.FileName);
                }
                else
                {
                    throw new NotSupportedException("Platform not supported");
                }
            }

            return new UnityProcessFacade(processFacade, monoLibraryOffsets);
        }

        private Process GetMTGAProcess()
        {
            Process[] processes = Process.GetProcesses();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach(Process process in processes) 
                {
                    if (process.ProcessName == "MTGA")
                    {
                        return process;
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                foreach(Process process in processes) 
                {
                    if (process.ProcessName == "MTGA.exe")
                    {
                        string maps = File.ReadAllText($"/proc/{process.Id}/maps");
                        if (!string.IsNullOrWhiteSpace(maps)) 
                        {
                            return process;
                        }
                    }
                }
            }
            else
            {
                throw new NotSupportedException("Platform not supported");
            }

            return null;
        }

        private bool CheckForUpdates()
        {            
            try 
            {
                string latestVersionJSON = GetLatestVersionJSON();            
                DaemonVersion latestVersion = JsonConvert.DeserializeObject<DaemonVersion>(latestVersionJSON);
                                
                Console.WriteLine($"Latest version = {latestVersion.TagName}");
                if(currentVersion.CompareTo(new Version(latestVersion.TagName)) < 0)
                {                    
                    Task.Run(() => Update(latestVersion));
                    return true;
                }
            } 
            catch (Exception ex)
            {
                Console.WriteLine($"Could not get latest version {ex}");
            }
            return false;
        }

        private void Update(DaemonVersion latestVersion)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                updating = true;
                Console.WriteLine("Updating...");
                string targetAssetName;
                targetAssetName = "mtga-tracker-daemon-Linux.tar.gz";
                Asset asset = latestVersion.Assets.Find(asset => asset.Name == targetAssetName);

                string tmpDir = "/tmp/mtga-tracker-dameon";
                Directory.CreateDirectory(tmpDir);
                string file = Path.Combine(tmpDir, asset.Name);
                using (var client = new WebClient())
                {
                    client.DownloadFile(asset.BrowserDownloadUrl, file);
                }

                ExtractTGZ(file, tmpDir);
                
                DirectoryInfo currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
                RemoveDirectoryContentsRecursive(currentDir);

                Copy(Path.Combine(tmpDir, "bin"), currentDir.FullName);
                RemoveDirectoryContentsRecursive(new DirectoryInfo(tmpDir));
                Console.WriteLine("Updated correctly");

                string binary = Path.Combine(currentDir.FullName, "mtga-tracker-daemon");
                ProcessStartInfo startInfo = new ProcessStartInfo()
                {
                    FileName = "/bin/bash", Arguments = $"-c \"chmod +x {binary}\" && systemctl restart mtga-trackerd.service", 
                };
                Process proc = new Process() { StartInfo = startInfo, };
                proc.Start();
                runServer = false;
                listener.Stop();
                Console.WriteLine("Restarting...");
            }
        }

        private void RemoveDirectoryContentsRecursive(DirectoryInfo directory) {
            FileInfo[] oldFiles = directory.GetFiles();
            foreach (FileInfo oldFile in oldFiles)
            {
                oldFile.Delete();
            }

            foreach(DirectoryInfo childDirectory in directory.GetDirectories())
            {
                RemoveDirectoryContentsRecursive(childDirectory);
                childDirectory.Delete();
            }
        }

        private string GetLatestVersionJSON()
        {
            string url = "https://api.github.com/repos/frcaton/mtga-tracker-daemon/releases/latest";
            var request = (HttpWebRequest)HttpWebRequest.Create(url);

            request.ContentType = "application/json";
            request.Method = "GET";
            request.UserAgent = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.106 Safari/537.36";

            var response = (HttpWebResponse)request.GetResponse();
            using (StreamReader Reader = new StreamReader(response.GetResponseStream()))
            {
                return Reader.ReadToEnd();
            }
        }

        private void ExtractTGZ(String gzArchiveName, String destFolder)
        {
            Stream inStream = File.OpenRead(gzArchiveName);
            Stream gzipStream = new GZipInputStream(inStream);

            TarArchive tarArchive = TarArchive.CreateInputTarArchive(gzipStream);
            tarArchive.ExtractContents(destFolder);
            tarArchive.Close();

            gzipStream.Close();
            inStream.Close();
        }

        private void Copy(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach(var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));
            }

            foreach(var directory in Directory.GetDirectories(sourceDir))
            {
                Copy(directory, Path.Combine(targetDir, Path.GetFileName(directory)));
            }
        }

    }

}
