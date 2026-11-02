#if DESKTOPGL && NVG_STEAM
#error NVG_STEAM is not supported on the DesktopGL build. Steam integration stays off. Unset NvgSteam / NVG_STEAM.
#endif
#if DESKTOPGL && !NVG_STEAM
using System;
using System.Collections.Generic;

namespace Steamworks
{
    /// <summary>
    /// Experimental DesktopGL stand-in. The vendored Steamworks.NET.dll is a
    /// PE32+ AMD64 image, so the arm64 runtime cannot load it. These types
    /// keep the existing call sites compiling. They do not P/Invoke steam_api.
    /// </summary>
    public struct AppId_t
    {
        public ulong m_AppId;
        public AppId_t(ulong appId) { m_AppId = appId; }
        public override string ToString() => m_AppId.ToString();
    }

    public struct PublishedFileId_t
    {
        public ulong m_PublishedFileId;
        public PublishedFileId_t(ulong id) { m_PublishedFileId = id; }
        public override string ToString() => m_PublishedFileId.ToString();
        public string ToString(IFormatProvider? provider) => m_PublishedFileId.ToString(provider);
        public static bool operator ==(PublishedFileId_t a, PublishedFileId_t b) => a.m_PublishedFileId == b.m_PublishedFileId;
        public static bool operator !=(PublishedFileId_t a, PublishedFileId_t b) => !(a == b);
        public override bool Equals(object? obj) => obj is PublishedFileId_t other && this == other;
        public override int GetHashCode() => m_PublishedFileId.GetHashCode();
    }

    public struct SteamAPICall_t
    {
        public ulong m_SteamAPICall;
    }

    public struct UGCUpdateHandle_t
    {
        public ulong m_UGCUpdateHandle;
    }

    public struct CSteamID
    {
        public override string ToString() => "0";
    }

    public enum EResult
    {
        k_EResultOK = 1,
    }

    public enum EWorkshopFileType
    {
        k_EWorkshopFileTypeCommunity = 0,
    }

    public enum ERemoteStoragePublishedFileVisibility
    {
        k_ERemoteStoragePublishedFileVisibilityPublic = 0,
        k_ERemoteStoragePublishedFileVisibilityPrivate = 2,
    }

    public struct UserStatsReceived_t
    {
        public EResult m_eResult;
        public ulong m_nGameID;
    }

    public struct DownloadItemResult_t
    {
        public PublishedFileId_t m_nPublishedFileId;
        public EResult m_eResult;
    }

    public struct CreateItemResult_t
    {
        public EResult m_eResult;
        public PublishedFileId_t m_nPublishedFileId;
    }

    public struct SubmitItemUpdateResult_t
    {
        public EResult m_eResult;
        public PublishedFileId_t m_nPublishedFileId;
    }

    public class Callback<T>
    {
        public static Callback<T> Create(Action<T> handler) => new();
    }

    public class CallResult<T>
    {
        public static CallResult<T> Create(Action<T, bool> handler) => new();
        public void Set(SteamAPICall_t call) { }
    }

    public static class SteamAPI
    {
        public static bool IsSteamRunning() => false;
        public static bool RestartAppIfNecessary(AppId_t appId) => false;
        public static bool Init() => false;
        public static void Shutdown() { }
        public static void RunCallbacks() { }
    }

    public static class SteamApps
    {
        public static string GetCurrentGameLanguage() => "";
    }

    public static class SteamUser
    {
        public static CSteamID GetSteamID() => new();
    }

    public static class SteamFriends
    {
        public static bool SetRichPresence(string key, string value) => false;
    }

    public static class SteamUtils
    {
        public static AppId_t GetAppID() => new(0);
    }

    public static class SteamUserStats
    {
        public static bool RequestCurrentStats() => false;
        public static uint GetNumAchievements() => 0;
        public static string GetAchievementName(uint index) => "";
        public static bool GetAchievement(string name, out bool achieved)
        {
            achieved = false;
            return false;
        }
        public static bool SetAchievement(string name) => false;
    }

    public static class SteamUGC
    {
        public static bool GetItemInstallInfo(PublishedFileId_t id, out ulong sizeOnDisk, out string folder, uint folderSize, out uint timestamp)
        {
            sizeOnDisk = 0;
            folder = "";
            timestamp = 0;
            return false;
        }
        public static uint GetNumSubscribedItems() => 0;
        public static uint GetSubscribedItems(PublishedFileId_t[] items, uint maxEntries) => 0;
        public static bool DownloadItem(PublishedFileId_t id, bool highPriority) => false;
        public static SteamAPICall_t CreateItem(AppId_t appId, EWorkshopFileType type) => new();
        public static UGCUpdateHandle_t StartItemUpdate(AppId_t appId, PublishedFileId_t id) => new();
        public static bool SetItemTitle(UGCUpdateHandle_t handle, string title) => false;
        public static bool SetItemDescription(UGCUpdateHandle_t handle, string description) => false;
        public static bool SetItemVisibility(UGCUpdateHandle_t handle, ERemoteStoragePublishedFileVisibility visibility) => false;
        public static bool SetItemContent(UGCUpdateHandle_t handle, string contentFolder) => false;
        public static bool SetItemPreview(UGCUpdateHandle_t handle, string previewFile) => false;
        public static bool SetItemTags(UGCUpdateHandle_t handle, string[] tags) => false;
        public static SteamAPICall_t SubmitItemUpdate(UGCUpdateHandle_t handle, string changeNote) => new();
    }
}
#endif
