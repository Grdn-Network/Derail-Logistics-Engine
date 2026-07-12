using DV.Logic.Job;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Data
{
    public enum TrackRole
    {
        Unknown,
        Inbound,           // -I  (REGULAR_IN_TYPE)
        Outbound,          // -O  (REGULAR_OUT_TYPE)
        Storage,           // -S  (STORAGE_TYPE)
        StoragePassenger,  // -SP (STORAGE_PASSENGER_TYPE), pax overflow candidate
        Loading,           // -L  (LOADING_TYPE)
        MainLine,          // -M  (MAIN_LINE_TYPE)
        Parking,           // -P  (PARKING_TYPE)
    }

    public static class TrackClassifier
    {
        // TrackID.FullID format: "{yardId}-{subYardId}-{orderNumber}-{trackType}"
        // e.g. "HB-E-08-I", "HB-E-03-O", "HB-E-02-S"
        public static TrackRole GetRole(Track track)
        {
            var typeCode = GetTypeCode(track);
            return typeCode switch
            {
                "I"  => TrackRole.Inbound,
                "O"  => TrackRole.Outbound,
                "S"  => TrackRole.Storage,
                "SP" => TrackRole.StoragePassenger,
                "L"  => TrackRole.Loading,
                "M"  => TrackRole.MainLine,
                "P"  => TrackRole.Parking,
                _    => TrackRole.Unknown,
            };
        }

        public static string GetTypeCode(Track track)
        {
            if (track?.ID == null) return "";
            var full = track.ID.FullID ?? "";
            if (string.IsNullOrEmpty(full)) return "";
            // Last hyphen-segment is the type code
            var idx = full.LastIndexOf('-');
            return idx >= 0 ? full.Substring(idx + 1) : "";
        }

        public static string GetYardId(Track track) => track?.ID?.yardId ?? "";

        public static bool IsInbound(Track t)  => GetRole(t) == TrackRole.Inbound;
        public static bool IsOutbound(Track t) => GetRole(t) == TrackRole.Outbound;
        public static bool IsStorage(Track t)  => GetRole(t) == TrackRole.Storage;
        public static bool IsPaxStorage(Track t) => GetRole(t) == TrackRole.StoragePassenger;

        /// <summary>
        /// All inbound tracks at the station's logic yard that have free space.
        /// </summary>
        public static List<Track> GetInboundTracks(StationController station) =>
            GetTracksOfRole(station, TrackRole.Inbound);

        /// <summary>
        /// All outbound tracks at the station's logic yard.
        /// </summary>
        public static List<Track> GetOutboundTracks(StationController station) =>
            GetTracksOfRole(station, TrackRole.Outbound);

        /// <summary>
        /// All storage tracks. If allowPaxOverflow is true and pax mod is not loaded,
        /// also includes -SP tracks.
        /// </summary>
        public static List<Track> GetStorageTracks(StationController station,
                                                    bool allowPaxOverflow = true)
        {
            var tracks = GetTracksOfRole(station, TrackRole.Storage);
            if (allowPaxOverflow && !PaxModActive())
                tracks.AddRange(GetTracksOfRole(station, TrackRole.StoragePassenger));
            return tracks;
        }

        private static List<Track> GetTracksOfRole(StationController station, TrackRole role)
        {
            if (station?.logicStation?.yard == null) return new List<Track>();
            var yard = station.logicStation.yard;

            // Yard exposes tracks grouped by function; use those directly.
            // StoragePassenger (-SP) tracks are a sub-set of StorageTracks identified
            // by their type-code suffix; filter within that list.
            switch (role)
            {
                case TrackRole.Inbound:
                    return yard.TransferInTracks ?? new List<Track>();
                case TrackRole.Outbound:
                    return yard.TransferOutTracks ?? new List<Track>();
                case TrackRole.Storage:
                    return (yard.StorageTracks ?? new List<Track>())
                        .Where(t => GetTypeCode(t) != "SP")
                        .ToList();
                case TrackRole.StoragePassenger:
                    return (yard.StorageTracks ?? new List<Track>())
                        .Where(t => GetTypeCode(t) == "SP")
                        .ToList();
                default:
                    return new List<Track>();
            }
        }

        private static bool PaxModActive()
        {
            foreach (var mod in UnityModManagerNet.UnityModManager.modEntries)
                if (mod.Info.Id == "PassengerJobs" && mod.Active)
                    return true;
            return false;
        }
    }
}
