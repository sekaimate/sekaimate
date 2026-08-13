using Basis.Network.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static BasisNetworkCore.Serializable.SerializableBasis;

namespace BasisNetworkCore
{
    public static class BasisNetworkIDDatabase
    {
        public static ConcurrentDictionary<string, ushort> UshortNetworkDatabase = new ConcurrentDictionary<string, ushort>();
        private static readonly object AssignmentLock = new object();
        private static int counter = -1; // Start at -1 so the first increment becomes 0
        private static int exhaustedLogged;
        public static void AddOrFindNetworkID(NetPeer NetPeer, string UniqueStringID)
        {
            ushort value;
            bool assignedNewId;
            lock (AssignmentLock)
            {
                assignedNewId = !UshortNetworkDatabase.TryGetValue(UniqueStringID, out value);
                if (assignedNewId)
                {
                    int newCounter = Interlocked.Increment(ref counter);
                    if (newCounter > ushort.MaxValue)
                    {
                        Interlocked.Decrement(ref counter);
                        if (Interlocked.Exchange(ref exhaustedLogged, 1) == 0)
                        {
                            BNL.LogError($"NetID space exhausted ({ushort.MaxValue} ids assigned since the server was last empty); dropping request for {UniqueStringID}.");
                        }
                        return;
                    }

                    value = (ushort)newCounter;
                    UshortNetworkDatabase[UniqueStringID] = value;
                }
            }

            if (!assignedNewId)
            {
                ServerNetIDMessage SNIM = new ServerNetIDMessage
                {
                    NetIDMessage = new NetIDMessage() { playerID = UniqueStringID },
                    UshortUniqueIDMessage = new UshortUniqueIDMessage() { UniqueIDUshort = value }
                };
                NetDataWriter Writer = NetworkServer.RentWriter();
                SNIM.Serialize(Writer);
                NetworkServer.TrySend(NetPeer, Writer, BasisNetworkCommons.netIDAssignChannel, DeliveryMethod.ReliableOrdered);
                NetworkServer.ReturnWriter(Writer);
                BNL.Log($"Sent existing NetID ({value}) for {UniqueStringID} to peer {NetPeer.Id}");
            }
            else
            {
                BNL.Log($"New ID {value} assigned to {UniqueStringID}");
                ServerNetIDMessage SUIMA = new ServerNetIDMessage
                {
                    NetIDMessage = new NetIDMessage() { playerID = UniqueStringID },
                    UshortUniqueIDMessage = new UshortUniqueIDMessage() { UniqueIDUshort = value }
                };
                NetDataWriter Writer = NetworkServer.RentWriter();
                SUIMA.Serialize(Writer);

                NetworkServer.BroadcastMessageToClients(Writer, BasisNetworkCommons.netIDAssignChannel, NetworkServer.PeerSnapshot, DeliveryMethod.ReliableOrdered);
                NetworkServer.ReturnWriter(Writer);
                BNL.Log($"Broadcasted new ID ({value}) for {UniqueStringID} to all connected peers.");
            }
        }

        public static bool GetAllNetworkID(out List<ServerNetIDMessage> ServerUniqueIDMessages)
        {
            ServerUniqueIDMessages = new List<ServerNetIDMessage>();
            foreach (KeyValuePair<string, ushort> pair in UshortNetworkDatabase)
            {
                ServerNetIDMessage SUIM = new ServerNetIDMessage
                {
                    NetIDMessage = new NetIDMessage() { playerID = pair.Key },
                    UshortUniqueIDMessage = new UshortUniqueIDMessage() { UniqueIDUshort = pair.Value }
                };
                ServerUniqueIDMessages.Add(SUIM);
            }
            int Count = ServerUniqueIDMessages.Count;
            return Count != 0;
        }
        public static void RemoveUshortNetworkID(ushort netID)
        {
            BNL.Log($"Attempting to remove NetID: {netID}");
            // Remove based on value (ushort ID)
            var itemToRemove = UshortNetworkDatabase.FirstOrDefault(kvp => kvp.Value == netID);
            if (!string.IsNullOrEmpty(itemToRemove.Key))
            {
                if (UshortNetworkDatabase.TryRemove(itemToRemove.Key, out _))
                {
                    BNL.Log($"Successfully removed NetID: {netID} associated with UniqueStringID: {itemToRemove.Key}");
                }
                else
                {
                    BNL.Log($"Failed to remove NetID: {netID} (concurrent operation may have interfered)");
                }
            }
            else
            {
                BNL.Log($"NetID {netID} not found in the database.");
            }
        }

        public static void Reset()
        {
            BNL.Log("Resetting BasisNetworkIDDatabase...");
            UshortNetworkDatabase.Clear();
            Interlocked.Exchange(ref counter, -1);
            Interlocked.Exchange(ref exhaustedLogged, 0);
            BNL.Log("Database reset complete. Counter set to -1.");
        }
    }
}
