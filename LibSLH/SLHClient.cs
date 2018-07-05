using KdTree;
using KdTree.Math;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibSLH
{
    public class SLHClient : GridClient
    {
        private KdTree<float, Primitive> ObjectProximalLookup;
        private Dictionary<Primitive, float[]> ObjectPositions;

        public SLHClient() : base()
        {
            ObjectProximalLookup = new KdTree<float, Primitive>(3, new FloatMath(), AddDuplicateBehavior.Update);
            ObjectPositions = new Dictionary<Primitive, float[]>();
            Objects.ObjectUpdate += HandleObjectUpdate;
            //Self.IM += HandleInstantMessage;
        }

        private void HandleObjectUpdate(object sender, PrimEventArgs e)
        {
            if (e.IsAttachment || e.Prim.IsAttachment)
                return;
            var prim = e.Prim;
            if (ObjectPositions.TryGetValue(prim, out float[] old_point))
                ObjectProximalLookup.RemoveAt(old_point);
            var position = prim.Position;
            //Logger.Log(position, Helpers.LogLevel.Debug);
            float[] new_point = new float[] { position.X, position.Y, position.Z };
            ObjectPositions[prim] = new_point;
            ObjectProximalLookup.Add(new_point, prim);
        }

        public async Task<List<DirectoryManager.AgentSearchData>> SearchAvatarsByName(string avatar_name)
        {
            return await Task.Run(() =>
            {
                UUID query_id = new UUID();
                List<DirectoryManager.AgentSearchData> avatars = null;
                var reset = new AutoResetEvent(false);
                EventHandler<DirPeopleReplyEventArgs> reply_handler = (sender, args) =>
                {
                    if (args.QueryID == query_id)
                    {
                        avatars = args.MatchedPeople;
                        reset.Set();
                    }
                };
                Directory.DirPeopleReply += reply_handler;
                query_id = Directory.StartPeopleSearch(avatar_name, 0);
                // TODO: use a different timeout parameter
                reset.WaitOne(Settings.RESEND_TIMEOUT);
                return avatars;
            });
        }

        public IEnumerable<Avatar> GetAllAvatars()
        {
            return Network.Simulators.SelectMany(s => s.ObjectsAvatars.Copy().Values);
        }

        public Primitive GetObjectNearestPoint(Vector3 point)
        {
            var point_key = new[] { point.X, point.Y, point.Z };
            var node = ObjectProximalLookup.GetNearestNeighbours(point_key, 1);
            var prim = node[0].Value;
            return prim;
        }

        public Primitive.ObjectProperties RequestObjectPropertiesFamilyBlocking(UUID object_id)
        {
            var simulator = Network.CurrentSim; // TODO
            var reset = new AutoResetEvent(false);
            EventHandler<ObjectPropertiesFamilyEventArgs> handler = null;
            Primitive.ObjectProperties properties = null;
            handler = (sender, e) =>
            {
                if (e.Properties.ObjectID == object_id)
                {
                    properties = e.Properties;
                    Objects.ObjectPropertiesFamily -= handler;
                    reset.Set();
                }
            };
            Objects.ObjectPropertiesFamily += handler;
            Objects.RequestObjectPropertiesFamily(simulator, object_id);
            reset.WaitOne(Settings.RESEND_TIMEOUT);
            return properties;
        }

        public async Task<Primitive.ObjectProperties> RequestObjectPropertiesFamily(UUID object_id)
        {
            return await Task.Run(() => RequestObjectPropertiesFamilyBlocking(object_id));
        }

        public void Say(string message)
        {
            Self.Chat(message, 0, ChatType.Normal);
        }

        public void SendIM(UUID recipient, string message)
        {
            Self.InstantMessage(recipient, message);
        }

        public async void OnTeleportToAvatar(string avatar_name)
        {
            try
            {
                var search_avatars = await SearchAvatarsByName(avatar_name);
                var avatar_data = search_avatars.First(a => $"{a.FirstName} {a.LastName}" == avatar_name);
                var agent_id = avatar_data.AgentID;
                //var region_avatars = GetAllAvatars().ToArray();
                var agent_simulator = Network.Simulators
                    .First(s => s.AvatarPositions.ContainsKey(agent_id));
                var agent_position = agent_simulator.AvatarPositions[agent_id];
                var avatar_forward = Vector3.UnitX;
                Self.Teleport(agent_simulator.Handle, agent_position, avatar_forward);
            }
            catch (Exception ex)
            {
                Logger.Log("Could not teleport to avatar.", Helpers.LogLevel.Error, ex);
            }
        }
    }
}