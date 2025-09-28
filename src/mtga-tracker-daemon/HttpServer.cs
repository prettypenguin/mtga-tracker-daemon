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
using MTGATrackerDaemon.Controllers;

namespace MTGATrackerDaemon
{
    public class HttpServer
    {
        private HttpListener listener;

        private bool runServer = true;

        private Version currentVersion;

        private bool updating = false;

        // Controllers
        private StatusController statusController;
        private CardsController cardsController;
        private AllCardsController allCardsController;
        private PlayerController playerController;
        private InventoryController inventoryController;
        private EventsController eventsController;
        private MatchStateController matchStateController;
        private ExplorerController explorerController;
        private ShutdownController shutdownController;
        private UpdatesController updatesController;
            
        public void Start(string url)
        {
            var assembly = Assembly.GetExecutingAssembly();
            currentVersion = assembly.GetName().Version;
            Console.WriteLine($"Current version = {currentVersion}");

            CheckForUpdatesInternal();

            // Initialize controllers
            InitializeControllers();

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

        private void InitializeControllers()
        {
            statusController = new StatusController(this);
            cardsController = new CardsController(this);
            allCardsController = new AllCardsController(this);
            playerController = new PlayerController(this);
            inventoryController = new InventoryController(this);
            eventsController = new EventsController(this);
            matchStateController = new MatchStateController(this);
            explorerController = new ExplorerController(this);
            shutdownController = new ShutdownController(this);
            updatesController = new UpdatesController(this);
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

            // Handle POST requests
            if (request.HttpMethod == "POST")
            {
                if (request.Url.AbsolutePath == "/shutdown")
                {
                    responseJSON = shutdownController.HandleRequest();
                }
                else if(request.Url.AbsolutePath == "/checkForUpdates")
                {
                    responseJSON = updatesController.HandleRequest();
                }
            } 
            // Handle GET requests
            else if (request.HttpMethod == "GET")
            {
                if (request.Url.AbsolutePath == "/status")
                {
                    responseJSON = statusController.HandleRequest();
                }
                else if (request.Url.AbsolutePath == "/cards")
                {
                    responseJSON = cardsController.HandleRequest();
                }
                else if (request.Url.AbsolutePath == "/playerId") 
                {
                    responseJSON = playerController.HandleRequest();
                }
                else if (request.Url.AbsolutePath == "/inventory")
                {
                    responseJSON = inventoryController.HandleRequest();
                }
                else if (request.Url.AbsolutePath == "/events")
                {
                    responseJSON = eventsController.HandleRequest();
                }
                else if (request.Url.AbsolutePath == "/matchState")
                {
                    responseJSON = matchStateController.HandleRequest();
                }
                else if (request.Url.AbsolutePath == "/allCards")
                {
                    responseJSON = allCardsController.HandleRequest();
                }
                else if (request.Url.AbsolutePath.StartsWith("/explore"))
                {
                    // Explorer controller handles its own response
                    bool handled = await explorerController.HandleRequest(request, response);
                    if (handled) return;
                    responseJSON = $"{{\"error\":\"Explorer request failed\"}}";
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

        private object GetObjectPropertyInternal(object obj, string propertyName)
        {
            if (obj == null) return null;
            
            if (obj is IAssemblyImage assemblyImage)
            {
                return assemblyImage[propertyName];
            }
            else if (obj is ITypeDefinition typeDef)
            {
                // For type definitions, try multiple approaches
                try
                {
                    // First try to get as static field value
                    return typeDef.GetStaticValue<object>(propertyName);
                }
                catch
                {
                    // Try to find an instance of this class and access the field from it
                    try
                    {
                        // Look for common singleton patterns (expanded list)
                        object instance = null;
                        
                        // Try to find Instance, _instance, instance fields
                        var instanceFieldNames = new[] { 
                            "Instance", "_instance", "instance", "<Instance>k__BackingField",
                            "Current", "_current", "current", "<Current>k__BackingField",
                            "Singleton", "_singleton", "singleton", "<Singleton>k__BackingField",
                            "Default", "_default", "default", "<Default>k__BackingField"
                        };
                        
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
                    
                    // Try to get the field definition itself - this gives us type info even if we can't access the value
                    try
                    {
                        var field = typeDef.Fields.FirstOrDefault(f => f.Name == propertyName);
                        if (field != null)
                        {
                            return field;
                        }
                    }
                    catch { }
                    
                    // Last resort: try direct property access on the type definition
                    try
                    {
                        return typeDef[propertyName];
                    }
                    catch { }
                }
            }
            else if (obj is IManagedObjectInstance managedObj)
            {
                try
                {
                    return managedObj[propertyName];
                }
                catch
                {
                    // If direct access fails, try with GetValue
                    try
                    {
                        return managedObj.GetValue<object>(propertyName);
                    }
                    catch
                    {
                        // Return the field definition if we can't get the value but the field exists
                        var field = managedObj.TypeDefinition.Fields.FirstOrDefault(f => f.Name == propertyName);
                        return field;
                    }
                }
            }
            else if (obj is IFieldDefinition fieldDef)
            {
                // We're at a field definition level - can't navigate further unless it's a static field
                try
                {
                    if (fieldDef.TypeInfo.IsStatic)
                    {
                        return fieldDef.DeclaringType.GetStaticValue<object>(fieldDef.Name);
                    }
                }
                catch { }
                
                return null;
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

        private string GenerateExplorerHTMLInternal(object currentObject, string currentPath, string authority)
        {
            StringBuilder html = new StringBuilder();
            html.Append("<!DOCTYPE html><html><head><title>MTGA Memory Explorer</title>");
            html.Append("<style>body{font-family:Arial,sans-serif;margin:20px;line-height:1.4;} .path{background:#f0f0f0;padding:10px;margin-bottom:20px;border-radius:4px;} ");
            html.Append(".property{margin:3px 0;padding:2px 0;border-bottom:1px dotted #eee;} .clickable{color:#0066cc;text-decoration:underline;cursor:pointer;} .clickable:hover{background:#f0f8ff;} ");
            html.Append(".value{color:#006600;font-weight:bold;} .type{color:#666;font-style:italic;font-size:0.9em;} .back{margin-bottom:20px;} ");
            html.Append("strong{color:#333;} .property strong{color:#000080;} h3{color:#333;border-bottom:2px solid #ccc;padding-bottom:5px;}</style></head><body>");
            
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
                    html.Append("<h3>object is IAssemblyImage ; Assembly Image Properties:</h3>");
                    foreach (var typeDef in assemblyImage.TypeDefinitions.OrderBy(t => t.Name))
                    {
                        string newPath = string.IsNullOrEmpty(currentPath) ? typeDef.Name : currentPath + "|" + typeDef.Name;
                        html.Append($"<div class='property'><a class='clickable' href='http://{authority}/explore?path={Uri.EscapeDataString(newPath)}'>{HtmlEscape(typeDef.Name)}</a> <span class='type'>(Type)</span></div>");
                    }
                }
                if (currentObject is ITypeDefinition typeDefinition)
                {
                    html.Append("<h3>object is ITypeDefinition ; Type Fields:</h3>");
                    html.Append($"<div class='property'><strong>Type Info:</strong> {HtmlEscape(typeDefinition.FullName)} (Fields: {typeDefinition.Fields.Count})</div>");
                    html.Append($"<div class='property'><strong>Is Enum:</strong> {typeDefinition.IsEnum}, <strong>Is Value Type:</strong> {typeDefinition.IsValueType}</div>");
                    html.Append("<br/>");
                    
                    foreach (var fieldDef in typeDefinition.Fields)
                    {
                        string newPath = string.IsNullOrEmpty(currentPath) ? fieldDef.Name : currentPath + "|" + fieldDef.Name;
                        string linkPath = GetSmartNavigationPath(newPath, fieldDef.Name);
                        
                        // Enhanced field metadata
                        string fieldInfo = "";
                        if (fieldDef.TypeInfo.IsStatic) fieldInfo += " Static";
                        if (fieldDef.TypeInfo.IsConstant) fieldInfo += " Const";
                        string typeName = fieldDef.TypeInfo.TryGetTypeDefinition(out var typeDef) ? typeDef.Name : fieldDef.TypeInfo.TypeCode.ToString();
                        
                        try
                        {
                            // Always show the field as clickable - we'll try to navigate or get value
                            string fieldDisplay = $"<a class='clickable' href='http://{authority}/explore?path={Uri.EscapeDataString(linkPath)}'>{HtmlEscape(fieldDef.Name)}</a>";
                            
                            // Try to get static value if it's a static field
                            if (fieldDef.TypeInfo.IsStatic)
                            {
                                try
                                {
                                    var staticValue = typeDefinition.GetStaticValue<object>(fieldDef.Name);
                                    if (staticValue != null)
                                    {
                                        string valueStr = staticValue.ToString();
                                        if (valueStr.Length > 50) valueStr = valueStr.Substring(0, 50) + "...";
                                        fieldDisplay += $" = <span class='value'>{HtmlEscape(valueStr)}</span>";
                                        
                                        if (staticValue is IManagedObjectInstance || staticValue.GetType().IsArray)
                                        {
                                            fieldDisplay += $" → {HtmlEscape(staticValue.GetType().Name)}";
                                        }
                                    }
                                }
                                catch (Exception staticEx)
                                {
                                    fieldDisplay += $" <span style='color:orange'>[Static Value Error: {HtmlEscape(staticEx.Message.Split('\n')[0])}]</span>";
                                }
                            }
                            
                            html.Append($"<div class='property'>{fieldDisplay} <span class='type'>({HtmlEscape(typeName)}{fieldInfo})</span></div>");
                        }
                        catch (Exception ex)
                        {
                            html.Append($"<div class='property'><strong>{HtmlEscape(fieldDef.Name)}</strong>: <span style='color:red'>Error: {HtmlEscape(ex.Message)}</span> <span class='type'>({HtmlEscape(typeName)}{fieldInfo})</span></div>");
                        }
                    }
                }
                if (currentObject is IManagedObjectInstance managedObj)
                {
                    html.Append("<h3>object is IManagedObjectInstance ; Object Properties:</h3>");
                    foreach (var fieldDef in managedObj.TypeDefinition.Fields)
                    {
                        string newPath = string.IsNullOrEmpty(currentPath) ? fieldDef.Name : currentPath + "|" + fieldDef.Name;
                        string linkPath = GetSmartNavigationPath(newPath, fieldDef.Name);
                        
                        // Get field metadata
                        string fieldInfo = "";
                        if (fieldDef.TypeInfo.IsStatic) fieldInfo += " Static";
                        if (fieldDef.TypeInfo.IsConstant) fieldInfo += " Const";
                        string typeName = fieldDef.TypeInfo.TryGetTypeDefinition(out var typeDef) ? typeDef.Name : fieldDef.TypeInfo.TypeCode.ToString();
                        
                        try
                        {
                            var value = managedObj[fieldDef.Name];
                            
                            // Always show the field, but determine if it should be clickable
                            bool isNavigable = false;
                            string valueDisplay = "null";
                            string valueTypeInfo = "";
                            
                            if (value != null)
                            {
                                // Check if it's navigable (object instance or array)
                                if (value is IManagedObjectInstance || value.GetType().IsArray)
                                {
                                    isNavigable = true;
                                    valueTypeInfo = $" → {HtmlEscape(value.GetType().Name)}";
                                    valueDisplay = $"[{HtmlEscape(value.GetType().Name)}]";
                                }
                                else
                                {
                                    // Show the actual value for primitives/strings
                                    string valueStr = value.ToString();
                                    if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                                    valueDisplay = HtmlEscape(valueStr);
                                }
                            }
                            
                            // Always show field with complete metadata
                            if (isNavigable)
                            {
                                html.Append($"<div class='property'><a class='clickable' href='http://{authority}/explore?path={Uri.EscapeDataString(linkPath)}'>{HtmlEscape(fieldDef.Name)}</a>{valueTypeInfo} <span class='type'>({HtmlEscape(typeName)}{fieldInfo})</span></div>");
                            }
                            else
                            {
                                html.Append($"<div class='property'><strong>{HtmlEscape(fieldDef.Name)}</strong>: <span class='value'>{valueDisplay}</span> <span class='type'>({HtmlEscape(typeName)}{fieldInfo})</span></div>");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Still show the field even if we can't access its value
                            html.Append($"<div class='property'><strong>{HtmlEscape(fieldDef.Name)}</strong>: <span style='color:orange'>Inaccessible - {HtmlEscape(ex.Message)}</span> <span class='type'>({HtmlEscape(typeName)}{fieldInfo})</span>");
                            
                            // Try to make it clickable anyway - sometimes the value access fails but navigation works
                            try
                            {
                                html.Append($" [<a class='clickable' href='http://{authority}/explore?path={Uri.EscapeDataString(linkPath)}'>Try Navigate</a>]");
                            }
                            catch
                            {
                                // Ignore navigation attempt failure
                            }
                            html.Append("</div>");
                        }
                    }
                }
                if (currentObject is IFieldDefinition fieldDefinition)
                {
                    html.Append("<h3>object is IFieldDefinition ; Field Definition:</h3>");
                    html.Append($"<div class='property'>Name: <span class='value'>{HtmlEscape(fieldDefinition.Name)}</span></div>");
                    string typeName = fieldDefinition.TypeInfo.TryGetTypeDefinition(out var typeDef) ? typeDef.Name : fieldDefinition.TypeInfo.TypeCode.ToString();
                    html.Append($"<div class='property'>Type: <span class='value'>{HtmlEscape(typeName)}</span></div>");
                    html.Append($"<div class='property'>Is Static: <span class='value'>{fieldDefinition.TypeInfo.IsStatic}</span></div>");
                    html.Append($"<div class='property'>Is Constant: <span class='value'>{fieldDefinition.TypeInfo.IsConstant}</span></div>");
                    html.Append($"<div class='property'>Declaring Type: <span class='value'>{HtmlEscape(fieldDefinition.DeclaringType.FullName)}</span></div>");
                }
                if (currentObject != null && currentObject.GetType().IsArray)
                {
                    Array array = (Array)currentObject;
                    html.Append($"<h3>object is array ; Array Elements (Length: {array.Length}):</h3>");
                    
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

                html.Append($"<h3>Universal properties:</h3>");

                html.Append($"<div class='property'>Value: <span class='value'>{HtmlEscape(currentObject?.ToString() ?? "null")}</span></div>");
                html.Append($"<div class='property'>Type: <span class='type'>{HtmlEscape(currentObject?.GetType().Name ?? "null")}</span></div>");
                html.Append($"<div class='property'>Full Type: <span class='type'>{HtmlEscape(currentObject?.GetType().FullName ?? "null")}</span></div>");
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

        // Public methods for controllers to access
        public Version GetCurrentVersion() => currentVersion;
        public bool GetUpdating() => updating;
        public void SetRunServer(bool value) => runServer = value;
        public Process GetMTGAProcess() => GetMTGAProcessInternal();
        public IAssemblyImage CreateAssemblyImage() => CreateAssemblyImageInternal();
        public string JsonEscape(string text) => JsonEscapeInternal(text);
        public object GetObjectProperty(object obj, string propertyName) => GetObjectPropertyInternal(obj, propertyName);
        public string GenerateExplorerHTML(object currentObject, string currentPath, string authority) => GenerateExplorerHTMLInternal(currentObject, currentPath, authority);
        public bool CheckForUpdates() => CheckForUpdatesInternal();

        private string HtmlEscape(string text)
        {
            if (text == null) return "null";
            return text.Replace("&", "&amp;")
                      .Replace("<", "&lt;")
                      .Replace(">", "&gt;")
                      .Replace("\"", "&quot;")
                      .Replace("'", "&#39;");
        }

        private IAssemblyImage CreateAssemblyImageInternal()
        {
            UnityProcessFacade unityProcess = CreateUnityProcessFacadeInternal();
            return AssemblyImageFactory.Create(unityProcess, "Core");  
        }


        private string JsonEscapeInternal(string text)
        {
            if (text == null) return "null";
            return text.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\r", "\\r")
                      .Replace("\n", "\\n")
                      .Replace("\t", "\\t");
        }


    

        private UnityProcessFacade CreateUnityProcessFacadeInternal()
        {            
            Process mtgaProcess = GetMTGAProcessInternal();
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

        private Process GetMTGAProcessInternal()
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

        private bool CheckForUpdatesInternal()
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
