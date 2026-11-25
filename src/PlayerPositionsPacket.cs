using ProtoBuf;

namespace FarPlayerMarkers
{
    /// <summary>One online player's position, facing and chosen colours. Server to client.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PlayerPositionEntry
    {
        public string Uid;
        public string Name;
        public double X;
        public double Y;
        public double Z;
        public float Yaw;
        public string FillHex;
        public string BorderHex;
    }

    /// <summary>Every visible online player, sent on each update tick. Server to client.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PlayerPositionsPacket
    {
        public PlayerPositionEntry[] Players;
    }

    /// <summary>The colours a player picked for their own marker. Client to server.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PlayerColorPacket
    {
        public string FillHex;
        public string BorderHex;
    }

    /// <summary>Current server settings and whether this player may change them. Server to client.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ServerSettingsPacket
    {
        public bool IsAdmin;
        public int UpdateIntervalMs;
        public bool HideCrouchingPlayers;
        public bool ShareWaypoints;
    }

    /// <summary>New server settings from an admin's settings window. Client to server.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class SaveServerSettingsPacket
    {
        public int UpdateIntervalMs;
        public bool HideCrouchingPlayers;
        public bool ShareWaypoints;
    }

    /// <summary>One waypoint a player placed on their map. Server to client.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class SharedWaypoint
    {
        public double X;
        public double Y;
        public double Z;
        public string Title;
        public string Icon;
        public int Color;
        public bool Pinned;
        public string OwnerUid;
        public string OwnerName;
    }

    /// <summary>Every player's waypoints. Server to client, sent when they change.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class SharedWaypointsPacket
    {
        public SharedWaypoint[] Waypoints;
    }
}
