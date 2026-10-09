// <copyright file="PortalAccessSettings.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace PortalAccess
{
    using GameHelper.Plugin;

    public sealed class PortalAccessSettings : IPSettings
    {
        // Recovery requires a verified client profile even when explicitly enabled.
        public bool RestoreInteraction;
        public int RefreshIntervalMs = 1000;
        public int AreaDelayMs = 1500;
    }
}
