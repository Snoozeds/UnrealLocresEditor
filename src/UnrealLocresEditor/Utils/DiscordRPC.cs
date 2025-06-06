﻿using System;
using System.IO;
using DiscordRPC;
using UnrealLocresEditor.Utils;

namespace UnrealLocresEditor
{
    public class DiscordRPC
    {
        private AppConfig _appConfig;
        private bool rpcEnabled = false;
        public DiscordRpcClient client;
        public DateTime? editStartTime;
        public DateTime? idleStartTime;

        public void Initialize(string locresPath)
        {
            _appConfig = AppConfig.Instance;
            rpcEnabled = _appConfig.DiscordRPCEnabled;

            // Initialize Discord RPC client only if enabled
            if (rpcEnabled)
            {
                client = new DiscordRpcClient("1251663992162619472");

                client.OnReady += (sender, e) =>
                {
                    Console.WriteLine("Received Ready from user {0}", e.User.Username);
                };

                client.OnPresenceUpdate += (sender, e) =>
                {
                    Console.WriteLine("Received Update! {0}", e.Presence);
                };

                client.Initialize();
            }

            UpdatePresence(rpcEnabled, locresPath);
        }

        public void UpdatePresence(bool enabled, string locresPath)
        {
            // Load config if not already loaded
            _appConfig ??= AppConfig.Instance;

            if (enabled)
            {
                if (client == null || client.IsDisposed)
                {
                    client = new DiscordRpcClient("1251663992162619472");

                    client.OnReady += (sender, e) =>
                    {
                        Console.WriteLine("Received Ready from user {0}", e.User.Username);
                    };

                    client.OnPresenceUpdate += (sender, e) =>
                    {
                        Console.WriteLine("Received Update! {0}", e.Presence);
                    };

                    client.Initialize();
                }

                var presence = new RichPresence
                {
                    Details = _appConfig.DiscordRPCPrivacy
                        ? _appConfig.DiscordRPCPrivacyString
                        : (
                            locresPath == null
                                ? "Idling"
                                : $"Editing file: {Path.GetFileName(locresPath)}"
                        ),
                    Timestamps = editStartTime.HasValue
                        ? new Timestamps(editStartTime.Value)
                        : null,
                    Assets = new Assets { LargeImageKey = "ule-logo" },
                };

                client.SetPresence(presence);
            }
            else
            {
                if (client != null && !client.IsDisposed)
                {
                    client.ClearPresence();
                    client.Dispose();
                    client = null;
                }
            }
        }
    }
}
