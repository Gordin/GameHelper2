// <copyright file="RemoteObjectPropertyDetail.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using System.Reflection;

    /// <summary>
    ///     Stores some information about RemoteObject Property.
    /// </summary>
    internal struct RemoteObjectPropertyDetail
    {
        /// <summary>
        ///     Name of the property.
        /// </summary>
        public string Name;

        /// <summary>
        ///     Value of the property.
        /// </summary>
        public object Value;

        /// <summary>
        ///     ToImGui method of the property.
        /// </summary>
        public MethodInfo? ToImGui;
    }
}