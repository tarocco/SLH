using AustinHarris.JsonRpc;
using Fleck;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using Anemonis.JsonRpc;
using System.Reflection;
using System.Threading.Tasks;

namespace LibSLH
{
    public class SLH : IDisposable
    {
        //private const int RPCReceivingStreamSize = 1 << 20; // 1MB
        private readonly SLHClient Client;

        private readonly SLHWebSocketServer Server;
        private readonly string SessionId;
        private readonly JsonRpcSerializer JSONRPCSerializer;
        private readonly JsonRpcService RPCService;

        private static SLHConverter Converter;

        static SLH()
        {
            Converter = new SLHConverter();
        }

        public SLH(SLHClient client, SLHWebSocketServer server)
        {
            Client = client;
            Server = server;

            Server.ReceivedMessage += HandleReceivedMessage;

            Client.Network.LoginProgress += HandleLoginProgress;
            Client.Network.LoggedOut += HandleLoggedOut;

            SessionId = new Guid().ToString();

            // Important step
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                //Converters = new[] { JSONConverter }, // Pass the shared (static) converter
                Converters = new[] { new SLHJSONConverter() },
                Error = delegate (object sender, ErrorEventArgs args)
                {
                    // SERIALIZE ALL THE THINGS!
                    args.ErrorContext.Handled = true;
                },
            };

            // Bind this class to the JSON-RPC server
            ServiceBinder.BindService(SessionId, this);

            // Set up the JSON-RPC client serializer
            JSONRPCSerializer = new JsonRpcSerializer();
        }

        private static void AddClientToConverter(SLHClient client)
        {
            Converter.AddClient(client);
        }

        private static bool RemoveClientFromConverter(SLHClient client)
        {
            return Converter.RemoveClient(client);
        }

        private void HandleLoggedOut(object sender, LoggedOutEventArgs e)
        {
            RemoveClientFromConverter(Client);
        }

        private void HandleLoginProgress(object sender, LoginProgressEventArgs e)
        {
            if (e.Status == LoginStatus.Success)
            {
                AddClientToConverter(Client);
            }
        }

        [JsonRpcMethod("Client/Eval")]
        public object Client_Eval(string member_path, object[] args = null)
        {
            try
            {
                // JSON-RPC service will call Client_Eval from inside a Task, so this method block
                // Call EcalMemberPath synchronously (Task.Result)
                var result = Utility.EvalMemberPath(Client, member_path, args, false, Converter).Result;
                return result;
            }
            catch (Exception ex)
            {
                Logger.Log($"Exception thrown in {nameof(Client_Eval)}", Helpers.LogLevel.Error, ex);
                return null;
            }
        }

        [JsonRpcMethod("Client/Eval/Set")]
        public object Client_EvalSet(string member_path, object value)
        {
            try
            {
                return Utility.EvalMemberPath(Client, member_path, new object[] { value }, true, Converter).Result;
            }
            catch (Exception ex)
            {
                Logger.Log($"Exception thrown in {nameof(Client_EvalSet)}", Helpers.LogLevel.Error, ex);
                return null;
            }
        }

        private class BoundEventHandler
        {
            public readonly object Target;
            public readonly EventInfo EventInfo;
            public readonly Delegate Delegate;

            public BoundEventHandler(object target, EventInfo event_info, Delegate @delegate)
            {
                Target = target;
                EventInfo = event_info;
                Delegate = @delegate;
            }
        }

        private readonly Dictionary<string, BoundEventHandler> RemoteEventHandlers =
            new Dictionary<string, BoundEventHandler>();

        private readonly Dictionary<IWebSocketConnection, HashSet<string>> ConenctionToEventHandlers =
            new Dictionary<IWebSocketConnection, HashSet<string>>();

        private string CreateRequestJSON(string method_name, params object[] arguments)
        {
            var request = new JsonRpcRequest(0L, method_name, arguments);
            return JSONRPCSerializer.SerializeRequest(request);
        }

        [JsonRpcMethod("Client/Eval/AddEventHandler")]
        public async void Client_AddEventHandler(string event_member_path, string remote_id)
        {
            try
            {
                IWebSocketConnection connection;
                {
                    var current_context = JsonRpcContext.Current();
                    var current_context_value = current_context.Value;
                    var server_args = current_context_value as SLHWebSocketServer.MessageEventArgs;
                    connection = server_args?.Socket;
                }

                if (RemoteEventHandlers.ContainsKey(remote_id))
                    throw new ArgumentException($"{nameof(RemoteEventHandlers)} contains identical key");
                var bound_event_info = await Utility.EvalMemberInfoPath(Client, event_member_path, null, false, Converter);
                var target = bound_event_info.Target;
                var event_info = bound_event_info.MemberInfo as EventInfo;
                Delegate handler = null;
                handler = Utility.WrapDynamicDelegate(event_info.EventHandlerType, (objects) =>
                {
                    // Last-ditch effort (safety net)
                    if (!connection.IsAvailable)
                    {
                        Logger.Log("Removed event handler from unavailable RPC connection", Helpers.LogLevel.Warning);
                        event_info.RemoveEventHandler(bound_event_info.Target, handler);
                    }
                    try
                    {
                        var message = CreateRequestJSON(remote_id, objects);
                        connection.Send(message);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Exception thrown", Helpers.LogLevel.Error, ex);
                    }
                });
                RemoteEventHandlers[remote_id] = new BoundEventHandler(target, event_info, handler);
                event_info.AddEventHandler(bound_event_info.Target, handler);
            }
            catch (Exception ex)
            {
                Logger.Log($"Exception thrown", Helpers.LogLevel.Error, ex);
            }
        }

        [JsonRpcMethod("Client/Eval/RemoveEventHandler")]
        public async void Client_RemoveEventHandler(string event_member_path, string remote_id)
        {
            try
            {
                var bound_event_info = await Utility.EvalMemberInfoPath(Client, event_member_path, null, false, Converter);
                var target = bound_event_info.Target;
                var event_info = bound_event_info.MemberInfo as EventInfo;

                if (!RemoteEventHandlers.TryGetValue(remote_id, out BoundEventHandler bound_event_handler))
                    throw new ArgumentException($"{nameof(RemoteEventHandlers)} does not contain key");

                var handler = bound_event_handler.Delegate;
                event_info.AddEventHandler(bound_event_info.Target, handler);
                RemoteEventHandlers.Remove(remote_id);
            }
            catch (Exception ex)
            {
                Logger.Log($"Exception thrown in {nameof(Client_EvalSet)}", Helpers.LogLevel.Error, ex);
            }
        }

        private void HandleReceivedMessage(object sender, SLHWebSocketServer.MessageEventArgs args)
        {
            try
            {
                Task.Run(async () =>
                {
                    var result = await JsonRpcProcessor.Process(SessionId, args.Message, args);
                    await args.Socket.Send(result);
                });
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to process message.", Helpers.LogLevel.Error, ex);
            }
        }

        public void Dispose()
        {
            Server.ReceivedMessage -= HandleReceivedMessage;
            RemoveClientFromConverter(Client);
        }
    }
}