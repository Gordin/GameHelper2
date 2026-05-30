// <copyright file="DiesAfterTime.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;

    /// <summary>
    ///     The <see cref="DiesAfterTime" /> component in the entity.
    /// </summary>
    public class DiesAfterTime : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DiesAfterTime" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="DiesAfterTime" /> component.</param>
        public DiesAfterTime(IntPtr address)
            : base(address) { }
    }
}