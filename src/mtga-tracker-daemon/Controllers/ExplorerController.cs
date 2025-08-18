using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using HackF5.UnitySpy;

namespace MTGATrackerDaemon.Controllers
{
    public class ExplorerController
    {
        private readonly HttpServer _server;

        public ExplorerController(HttpServer server)
        {
            _server = server;
        }

        public async Task<bool> HandleRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                DateTime startTime = DateTime.Now;
                IAssemblyImage assemblyImage = _server.CreateAssemblyImage();
                
                string path = request.QueryString["path"] ?? "";
                string[] pathParts = string.IsNullOrEmpty(path) ? new string[0] : path.Split('|');
                
                object currentObject = assemblyImage;
                string currentPath = "";
                
                foreach (string part in pathParts)
                {
                    if (!string.IsNullOrEmpty(part))
                    {
                        currentObject = _server.GetObjectProperty(currentObject, part);
                        currentPath += (string.IsNullOrEmpty(currentPath) ? "" : "|") + part;
                    }
                }
                
                string htmlResponse = _server.GenerateExplorerHTML(currentObject, currentPath, request.Url.Authority);
                
                TimeSpan ts = (DateTime.Now - startTime);
                
                byte[] htmlData = Encoding.UTF8.GetBytes(htmlResponse);
                response.AddHeader("Access-Control-Allow-Origin", "*");
                response.AddHeader("Access-Control-Allow-Methods", "*");
                response.AddHeader("Access-Control-Allow-Headers", "*");
                
                response.ContentType = "text/html";
                response.ContentEncoding = Encoding.UTF8;
                response.ContentLength64 = htmlData.LongLength;
                
                await response.OutputStream.WriteAsync(htmlData, 0, htmlData.Length);
                response.Close();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}