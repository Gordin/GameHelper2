// <copyright file="EntityFilterType.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteEnums.Entity
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    [JsonConverter(typeof(StringEnumConverter))]
    public enum EntityFilterType
    {
        PATH,
        PATHANDRARITY,
        MOD,
        MODANDRARITY,
        PATHANDSTAT,
    }
}
