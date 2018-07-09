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
        private KdTree<float, uint> ObjectProximalLookup;
        private Dictionary<uint, float[]> ObjectPositions;

        private readonly object ObjectUpdateLock = new object();

        private readonly Dictionary<UUID, uint> UUIDToLocalIdTable = new Dictionary<UUID, uint>();
        private readonly Dictionary<uint, UUID> LocalIdToUUIDTable = new Dictionary<uint, UUID>();

        private readonly Dictionary<uint, ulong> SimLookupTable = new Dictionary<uint, ulong>();
        private readonly HashLookup<uint, uint> LinkSetLookupTable = new HashLookup<uint, uint>();
        private readonly Dictionary<uint, uint> LinkSetParentTable = new Dictionary<uint, uint>();

        public SLHClient() : base()
        {
            ObjectProximalLookup = new KdTree<float, uint>(3, new FloatMath(), AddDuplicateBehavior.Update);
            ObjectPositions = new Dictionary<uint, float[]>();
            Objects.ObjectUpdate += HandleObjectUpdate;
            Objects.ObjectPropertiesUpdated += HandleObjectPropertiesUpdated;
            Objects.KillObject += HandleKillObject;
            Objects.KillObjects += HandleKillObjects;
            Objects.ObjectDataBlockUpdate += HandleObjectDataBlockUpdate;
            //Self.IM += HandleInstantMessage;
        }

        private void OnUpdateObjects(Primitive prim, ulong simulator_handle)
        {
            //if (prim.OwnerID == UUID.Zero)
            //    return;
            // Use lock because the ObjectUpdate event is raised from the networking thread
            lock (ObjectUpdateLock)
            {
                if (!prim.IsAttachment)
                {
                    if (ObjectPositions.TryGetValue(prim.LocalID, out float[] old_point))
                        ObjectProximalLookup.RemoveAt(old_point);
                    var position = prim.Position;
                    //Logger.Log(position, Helpers.LogLevel.Debug);
                    float[] new_point = new float[] { position.X, position.Y, position.Z };
                    ObjectPositions[prim.LocalID] = new_point;
                    ObjectProximalLookup.Add(new_point, prim.LocalID);
                }

                UUIDToLocalIdTable[prim.ID] = prim.LocalID;
                LocalIdToUUIDTable[prim.LocalID] = prim.ID;

                SimLookupTable[prim.LocalID] = simulator_handle;

                if (prim.ParentID == 0)
                    LinkSetLookupTable.Add(prim.LocalID, prim.LocalID);
                else
                {
                    LinkSetLookupTable.Add(prim.ParentID, prim.LocalID);
                    LinkSetParentTable[prim.LocalID] = prim.ParentID;
                }
            }
        }

        private void OnKillObject(uint local_id)
        {
            lock (ObjectUpdateLock)
            {
                var children = LinkSetLookupTable[local_id].Where(i => i != local_id);
                foreach (var id in children)
                    OnKillObject(id);

                if (ObjectPositions.TryGetValue(local_id, out float[] old_point))
                {
                    ObjectProximalLookup.RemoveAt(old_point);
                    ObjectPositions.Remove(local_id);
                }

                if (LocalIdToUUIDTable.TryGetValue(local_id, out UUID uuid))
                {
                    UUIDToLocalIdTable.Remove(uuid);
                    LocalIdToUUIDTable.Remove(local_id);
                }

                SimLookupTable.Remove(local_id);

                LinkSetLookupTable.RemoveAll(local_id);
                LinkSetParentTable.Remove(local_id);
            }
        }

        private void HandleKillObject(object sender, KillObjectEventArgs e)
        {
            OnKillObject(e.ObjectLocalID);
        }

        private void HandleKillObjects(object sender, KillObjectsEventArgs e)
        {
            foreach (var id in e.ObjectLocalIDs)
                OnKillObject(id);
        }

        private void HandleObjectUpdate(object sender, PrimEventArgs e)
        {
            OnUpdateObjects(e.Prim, e.Simulator.Handle);
            //Objects.SelectObject(e.Simulator, e.Prim.LocalID, true);
        }
        private void HandleObjectPropertiesUpdated(object sender, ObjectPropertiesUpdatedEventArgs e)
        {
            OnUpdateObjects(e.Prim, e.Simulator.Handle);
            //Objects.SelectObject(e.Simulator, e.Prim.LocalID, true);
        }
        private void HandleObjectDataBlockUpdate(object sender, ObjectDataBlockUpdateEventArgs e)
        {
            OnUpdateObjects(e.Prim, e.Simulator.Handle);
        }

        public IEnumerable<uint> GetLinkSetLocalIds(uint parent_id)
        {
            return LinkSetLookupTable[parent_id];
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

        public uint GetPrimLocalId(UUID id)
        {
            if (UUIDToLocalIdTable.TryGetValue(id, out uint value))
                return value;
            return default(uint);
        }

        public uint GetObjectNearestPoint(Vector3 point)
        {
            var point_key = new[] { point.X, point.Y, point.Z };
            var node = ObjectProximalLookup.GetNearestNeighbours(point_key, 1);
            var prim = node[0].Value;
            return prim;
        }

        protected Primitive.ObjectProperties RequestObjectPropertiesFamilyBlocking(UUID object_id)
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