using System;
using System.Globalization;
using UnityEngine;

namespace ClubhousePC
{
    public static class AdminShutdownGate
    {
        private const string ShutdownUntilKey = "BlubRoom.AdminShutdownUntilUtc";

        public static bool IsActive
        {
            get
            {
                var until = ShutdownUntilUtc;
                if (until > DateTime.UtcNow) return true;
                if (until != DateTime.MinValue) Clear();
                return false;
            }
        }

        public static string RemainingText
        {
            get
            {
                var remaining = ShutdownUntilUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return "0m 0s";
                return remaining.TotalMinutes >= 1
                    ? ((int)remaining.TotalMinutes) + "m " + remaining.Seconds + "s"
                    : remaining.Seconds + "s";
            }
        }

        public static void ActivateUntil(long utcTicks)
        {
            var latestAllowed = DateTime.UtcNow.AddHours(1).AddMinutes(1).Ticks;
            var safeTicks = Math.Min(utcTicks, latestAllowed);
            if (safeTicks <= DateTime.UtcNow.Ticks) return;
            PlayerPrefs.SetString(ShutdownUntilKey,
                safeTicks.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        private static DateTime ShutdownUntilUtc
        {
            get
            {
                var stored = PlayerPrefs.GetString(ShutdownUntilKey, "");
                if (!long.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture,
                        out var ticks) || ticks < DateTime.MinValue.Ticks ||
                    ticks > DateTime.MaxValue.Ticks)
                    return DateTime.MinValue;
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        private static void Clear()
        {
            PlayerPrefs.DeleteKey(ShutdownUntilKey);
            PlayerPrefs.Save();
        }
    }
}

