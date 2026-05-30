// <copyright file="NearbyZones.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>


namespace GameHelper.RemoteEnums.Entity
{
    using System;

    [Flags]
    public enum NearbyZones
    {
        None = 0,
        InnerCircle = 1,
        OuterCircle = 2
    }
}
