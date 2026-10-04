using GolfSim.Net;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The sim's room code and how its connection is doing, where players look for it: the main menu's top bar and
    /// the pause menu (labels "room-code" and "room-status"). Phones type the code once in their Settings. A server that
    /// turned the sim away (another sim already has the room) shows here in the warning colour. Refreshes itself.
    /// </summary>
    public class RoomBadge
    {
        const string Warning = "room__status--warning";
        const long RefreshMs = 500;

        readonly Label code, status;

        public RoomBadge(VisualElement root)
        {
            code = root.Q<Label>("room-code");
            status = root.Q<Label>("room-status");
            PlainText.Apply(status); // the server's words
            Refresh();
            status.schedule.Execute(Refresh).Every(RefreshMs);
        }

        void Refresh()
        {
            var connection = SimConnection.Instance;
            code.text = CodeText;
            status.text = StatusText(connection);
            status.EnableInClassList(Warning, connection == null || connection.Rejection != null || !connection.IsConnected);
        }

        /// <summary>"Room K7QF2".</summary>
        public static string CodeText => $"Room {SimRoom.Code}";

        /// <summary>One line under the code: connected (and what the phones do with it), connecting, offline or turned away.</summary>
        public static string StatusText(SimConnection connection)
        {
            if (!connection) return "Offline";
            if (connection.Rejection != null) return $"{connection.Rejection}. Trying again every {SimConnection.RejectedRetryDelay:0} s.";
            return connection.State switch
            {
                SimConnection.Status.Connected => "Phones: enter this code in Settings",
                SimConnection.Status.Connecting => "Connecting to the server...",
                _ => "Offline, retrying",
            };
        }
    }
}
