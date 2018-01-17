using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SLHBot
{
    static class Standalone
    {
        public static void StartListener(HttpListener listener)
        {
            Task.Run(async () =>
            {
                listener.Start();
                for (; ; )
                {
                    var context = await listener.GetContextAsync();
                    string response;
                    HttpStatusCode http_status_code;
                    try
                    {
                        var request_path = context.Request.RawUrl.TrimStart('/');
                        var root = new Uri(Directory.GetCurrentDirectory() + "/html/");
                        var file_path_uri = new Uri(root, request_path);
                        if (!root.IsBaseOf(file_path_uri))
                            throw new Exception("Bad file path URI");
                        if (file_path_uri.AbsolutePath.EndsWith("/"))
                            file_path_uri = new Uri(file_path_uri, "index.html");
                        using (var reader = new StreamReader(file_path_uri.AbsolutePath))
                            response = reader.ReadToEnd();
                        http_status_code = HttpStatusCode.OK;
                    }
                    catch (Exception ex)
                    {
                        response = $"{ex.Message}\r\n{ex.StackTrace}";
                        http_status_code = HttpStatusCode.InternalServerError;
                    }

                    context.Response.StatusCode = (int)http_status_code;
                    using (var writer = new StreamWriter(context.Response.OutputStream))
                    {
                        await writer.WriteAsync(response);
                        await writer.FlushAsync();
                    }
                }
            });
        }
    }
}
