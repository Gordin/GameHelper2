// <copyright file="NPC.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;

    /// <summary>
    ///     The <see cref="NPC" /> component in the entity.
    /// </summary>
    public class NPC : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="NPC" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="NPC" /> component.</param>
        public NPC(IntPtr address)
            : base(address) { }
    }
}