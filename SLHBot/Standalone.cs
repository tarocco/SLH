using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SLHBot
{
    internal class Standalone
    {
        private CancellationTokenSource CancelSource;
        public Task Task { get; private set; }

        public Task Run(string standalone_binding_address)
        {
            CancelSource = new CancellationTokenSource();
            var cancel_token = CancelSource.Token;
            // KISS HTTP server
            var listener = new HttpListener();
            var prefix = $"http://{standalone_binding_address}/";
            listener.Prefixes.Add(prefix);
            Task = Task.Run(async () =>
            {
                cancel_token.ThrowIfCancellationRequested();

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

                    if (cancel_token.IsCancellationRequested)
                        cancel_token.ThrowIfCancellationRequested();
                }
            }, cancel_token);

            Console.WriteLine($"Running standalone http server on {prefix}");

            return Task;
        }

        private Task Stop()
        {
            CancelSource.Cancel();
            return Task;
        }
    }
}
