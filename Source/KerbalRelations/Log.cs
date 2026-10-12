using System;
using UnityEngine;

namespace KerbalRelations
{
    internal static class Log
    {
        private const string Prefix = "[KerbalRelations] ";

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message, Exception ex = null)
        {
            Debug.LogError(Prefix + message + (ex != null ? ": " + ex : string.Empty));
        }
    }
}
