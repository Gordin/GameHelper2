// <copyright file="PortalAreaFence.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace PortalAccess
{
    using GameOffsets.Objects.States;
    using GameOffsets.Objects.States.InGameState;

    /// <summary>Checks native area/player/loading data again immediately before each write.</summary>
    internal sealed record PortalAreaFence(long Loading, long InGame, long Area, uint Hash, long Player, uint PlayerId, uint LoadingTime)
    {
        internal static bool TryCapture(IRemoteMemory memory, long loadingAddress, long gameAddress, out PortalAreaFence? fence)
        {
            fence = null;
            if (!memory.Read<AreaLoadingStateOffset>(loadingAddress, out var loading) || loading.IsLoading != 0 ||
                !memory.Read<InGameStateOffset>(gameAddress, out var game) ||
                !memory.Read<AreaInstanceOffsets>(game.AreaInstanceData.ToInt64(), out var area) || area.CurrentAreaHash == 0 ||
                !memory.Read<EntityOffsets>(area.PlayerInfo.LocalPlayerPtr.ToInt64(), out var player) || player.IsValid != 12) return false;
            fence = new(loadingAddress, gameAddress, game.AreaInstanceData.ToInt64(), area.CurrentAreaHash,
                area.PlayerInfo.LocalPlayerPtr.ToInt64(), player.Id, loading.TotalLoadingScreenTimeMs);
            return fence.IsCurrent(memory);
        }

        internal bool IsCurrent(IRemoteMemory memory) =>
            memory.Read<AreaLoadingStateOffset>(this.Loading, out var loading) && loading.IsLoading == 0 && loading.TotalLoadingScreenTimeMs == this.LoadingTime &&
            memory.Read<InGameStateOffset>(this.InGame, out var game) && game.AreaInstanceData.ToInt64() == this.Area &&
            memory.Read<AreaInstanceOffsets>(this.Area, out var area) && area.CurrentAreaHash == this.Hash && area.PlayerInfo.LocalPlayerPtr.ToInt64() == this.Player &&
            memory.Read<EntityOffsets>(this.Player, out var player) && player.Id == this.PlayerId && player.IsValid == 12;
    }
}
