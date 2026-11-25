using System;
using System.Globalization;

namespace FarPlayerMarkers
{
    /// <summary>
    /// Each player's own settings. Saved on their PC as ModConfig/FarPlayerMarkersConfig.json.
    /// </summary>
    public class ClientConfig
    {
        public string OwnFillColorHex = "#33D940";
        public string OwnBorderColorHex = "#000000";
        public string OtherFillColorHex = "#3399FF";
        public string OtherBorderColorHex = "#000000";

        /// <summary>Show other players in the colours they chose, instead of the "others" colours above.</summary>
        public bool UsePlayerSelectedColors = true;

        /// <summary>Marker size on the map before GUI scaling.</summary>
        public double MarkerSize = 20;

        /// <summary>Keep player markers on the edge of the map/minimap when they are outside the area shown.</summary>
        public bool KeepPlayersAtMapEdge = true;

        /// <summary>Show waypoints that other players have placed.</summary>
        public bool ShowOthersWaypoints = true;

        /// <summary>Waypoint marker size on the map before GUI scaling.</summary>
        public double WaypointSize = 16;
    }

    /// <summary>
    /// Server settings. Saved on the server as ModConfig/FarPlayerMarkersServerConfig.json.
    /// Admins can change them in-game from the .farmarkers window.
    /// </summary>
    public class ServerConfig
    {
        /// <summary>How often positions are sent to players, in milliseconds.</summary>
        public int UpdateIntervalMs = 1000;

        /// <summary>Leave crouching (sneaking) players off the map.</summary>
        public bool HideCrouchingPlayers = false;

        /// <summary>Send every player's waypoints to everyone.</summary>
        public bool ShareWaypoints = true;

        public const int MinIntervalMs = 250;
        public const int MaxIntervalMs = 10000;

        public void Clamp()
        {
            if (UpdateIntervalMs < MinIntervalMs) UpdateIntervalMs = MinIntervalMs;
            if (UpdateIntervalMs > MaxIntervalMs) UpdateIntervalMs = MaxIntervalMs;
        }
    }

    /// <summary>Hex colour helpers. Accepts #RGB, #RRGGBB and #RRGGBBAA, with or without the #.</summary>
    public static class ColorHex
    {
        public static bool TryParse(string hex, out double r, out double g, out double b, out double a)
        {
            r = g = b = 0;
            a = 1;

            string h = Normalize(hex);
            if (h == null) return false;

            r = Convert.ToInt32(h.Substring(1, 2), 16) / 255.0;
            g = Convert.ToInt32(h.Substring(3, 2), 16) / 255.0;
            b = Convert.ToInt32(h.Substring(5, 2), 16) / 255.0;
            if (h.Length == 9) a = Convert.ToInt32(h.Substring(7, 2), 16) / 255.0;
            return true;
        }

        /// <summary>Returns the colour as upper-case #RRGGBB or #RRGGBBAA, or null if it isn't a valid colour.</summary>
        public static string Normalize(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;

            string h = hex.Trim();
            if (h.StartsWith("#")) h = h.Substring(1);

            if (h.Length == 3)
            {
                h = new string(new char[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            }

            if (h.Length != 6 && h.Length != 8) return null;

            foreach (char c in h)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex) return null;
            }

            return "#" + h.ToUpper(CultureInfo.InvariantCulture);
        }
    }
}
