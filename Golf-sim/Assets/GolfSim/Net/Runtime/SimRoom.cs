using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GolfSim.Net
{
    /// <summary>
    /// The sim's room on the game server: a short code the TV shows and each phone types once in its Settings (so a
    /// second sim on the same server never steals this one's phones), and a permanent install id that tells the server
    /// a reconnect of this sim from another sim taking the room. Both are made the first time they're needed and kept
    /// in PlayerPrefs, like the sound settings. See Game-server/docs/PROTOCOL.md ("Rooms").
    /// </summary>
    public static class SimRoom
    {
        /// <summary>The characters of a code the sim makes: no 0/O, 1/I/L to misread on a TV across the room.</summary>
        public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        public const int CodeLength = 5, MinLength = 4, MaxLength = 8;
        const string CodeKey = "GolfSim.Room.Code", IdKey = "GolfSim.Room.InstallId";

        /// <summary>This sim's room code, e.g. "K7QF2" (made once, then kept).</summary>
        public static string Code
        {
            get
            {
                string saved = Normalize(PlayerPrefs.GetString(CodeKey, ""));
                if (IsValid(saved)) return saved;
                return Save(CodeKey, NewCode());
            }
        }

        /// <summary>This install's permanent id: 32 hex characters (made once, then kept).</summary>
        public static string InstallId
        {
            get
            {
                string saved = PlayerPrefs.GetString(IdKey, "");
                if (IsValidId(saved)) return saved;
                return Save(IdKey, Guid.NewGuid().ToString("N"));
            }
        }

        /// <summary>A room code as the server compares it: trimmed and upper case ("" for null).</summary>
        public static string Normalize(string code) => (code ?? "").Trim().ToUpperInvariant();

        /// <summary>4-8 of A-Z and 0-9, after Normalize.</summary>
        public static bool IsValid(string code)
        {
            if (code == null || code.Length < MinLength || code.Length > MaxLength) return false;
            foreach (char c in code)
                if (!IsUpperOrDigit(c)) return false;
            return true;
        }

        /// <summary>What the server takes as an install id: 1-64 of A-Z, a-z, 0-9, _ and -.</summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id)
                if (!(IsUpperOrDigit(c) || c >= 'a' && c <= 'z' || c == '_' || c == '-')) return false;
            return true;
        }

        /// <summary>A fresh random code of CodeLength characters from Alphabet.</summary>
        public static string NewCode()
        {
            var bytes = new byte[CodeLength];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var code = new StringBuilder(CodeLength);
            foreach (byte b in bytes) code.Append(Alphabet[b % Alphabet.Length]);
            return code.ToString();
        }

        static bool IsUpperOrDigit(char c) => c >= 'A' && c <= 'Z' || c >= '0' && c <= '9';

        static string Save(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
            return value;
        }
    }
}
