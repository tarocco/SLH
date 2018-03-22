using Fleck;
using LitJson;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace SLHBot
{
    internal class SLHWebSocketServer : IDisposable
    {
        private List<IWebSocketConnection> Sockets;
        private WebSocketServer Server;
        //private LocklessQueue<JsonData> MessageQueue;

        public class JSONMessageEventArgs : EventArgs
        {
            public JsonData Message;
        }

        public event EventHandler<JSONMessageEventArgs> ReceivedJSONMessage;

        public X509Certificate2 Certificate
        {
            get { return Server.Certificate; }
            set { Server.Certificate = value; }
        }

        public SLHWebSocketServer(string location)
        {
            Server = new WebSocketServer(location);
            Setup();
        }

        public SLHWebSocketServer(int port_number, string location)
        {
            Server = new WebSocketServer(port_number, location);
            Setup();
        }

        public void Dispose()
        {
            Server.Dispose();
        }

        private void Setup()
        {
            Server.SupportedSubProtocols = new[] { "SLH-Message-Protocol-0001" };
            //MessageQueue = new LocklessQueue<JsonData>();
            Sockets = new List<IWebSocketConnection>();
        }

        private void Config(IWebSocketConnection socket)
        {
            socket.OnOpen += () =>
            {
                Sockets.Add(socket);
            };

            socket.OnClose += () =>
            {
                Sockets.Remove(socket);
            };

            socket.OnMessage += message =>
            {
                try
                {
                    JsonData data;
                    try
                    {
                        data = JsonMapper.ToObject(message);
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException("Failed to parse JSON message.", ex);
                    }

                    var args = new JSONMessageEventArgs() { Message = data };
                    ReceivedJSONMessage?.Invoke(this, args);

                    //try
                    //{
                    //    MessageQueue.Enqueue(data);
                    //}
                    //catch (Exception ex)
                    //{
                    //    throw new ArgumentException("Could not enqueue message.", ex);
                    //}
                }
                catch (Exception ex)
                {
                    Logger.Log(ex.Message, Helpers.LogLevel.Error);
                }
            };
        }

        //public bool TryDequeueMessage(out JsonData message_body)
        //{
        //    return MessageQueue.TryDequeue(out message_body);
        //}

        public void Start()
        {
            Server.Start(Config);
        }

        public void BroadcastMessage(JsonData data)
        {
            foreach(var socket in Sockets)
            {
                socket.Send(data.ToJson());
            }
        }
    }
}