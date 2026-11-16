// <copyright file="Portal.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace PortalAccess
{
    using System;
    using GameHelper.RemoteObjects.Components;

    // Entity.TryGetComponent resolves the exact class name from the component map.
    // Only the existing ComponentHeader is read; no Portal-specific offsets are assumed.
    public sealed class Portal : ComponentBase
    {
        public Portal(IntPtr address) : base(address) { }
    }
}
